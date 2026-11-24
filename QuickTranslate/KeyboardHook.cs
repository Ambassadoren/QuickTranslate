using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Forms = System.Windows.Forms;

namespace QuickTranslate;

internal sealed class KeyboardHook : IDisposable
{
    private const int ResetMessage = 0x8001, RestartMessage = 0x8002, StopMessage = 0x8003, PingMessage = 0x8004;
    private readonly Thread worker;
    private readonly ManualResetEventSlim ready = new(false);
    private readonly Native.HookProc callback;
    private readonly DoubleCopyDetector detector = new();
    private readonly ConcurrentDictionary<int, (long At, TaskCompletionSource<long> Completion)> pings = new();
    private IntPtr handle, commandWindow;
    private Exception? startupError;
    private volatile bool enabled = true;
    private volatile bool desiredEnabled = true;
    private int interval = 650, disposed, pingSequence;
    public event Action? DoubleCopy;
    internal int WorkerThreadId { get; private set; }
    public bool Enabled
    {
        get => enabled;
        set { desiredEnabled = value; enabled = value; if (commandWindow != IntPtr.Zero) Native.PostMessage(commandWindow, ResetMessage, IntPtr.Zero, IntPtr.Zero); }
    }
    public int IntervalMs { get => Volatile.Read(ref interval); set => Volatile.Write(ref interval, Math.Clamp(value, 250, 1500)); }

    public KeyboardHook()
    {
        callback = OnKeyboard;
        worker = new Thread(Run) { IsBackground = true, Name = "QuickTranslate keyboard" };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        if (!ready.Wait(TimeSpan.FromSeconds(5)))
        {
            desiredEnabled = enabled = false;
            Interlocked.Exchange(ref disposed, 1);
            if (commandWindow != IntPtr.Zero) Native.PostMessage(commandWindow, StopMessage, IntPtr.Zero, IntPtr.Zero);
            throw new IOExceptionForHook("Поток горячих клавиш не запустился вовремя.");
        }
        if (startupError != null) { var error = startupError; Dispose(); throw new IOExceptionForHook("Не удалось включить Ctrl+C+C.", error); }
    }
    private void Run()
    {
        CommandWindow? window = null;
        try
        {
            WorkerThreadId = Environment.CurrentManagedThreadId;
            window = new CommandWindow(this);
            commandWindow = window.Handle;
            Install();
            if (Volatile.Read(ref disposed) != 0) return;
            ready.Set();
            Forms.Application.Run();
        }
        catch (Exception ex) { startupError = ex; ready.Set(); }
        finally
        {
            Remove();
            commandWindow = IntPtr.Zero;
            window?.Release();
            foreach (var item in pings.Values) item.Completion.TrySetCanceled();
            pings.Clear();
        }
    }
    private void Install()
    {
        Remove();
        ResetState();
        handle = Native.SetWindowsHookEx(13, callback, Native.GetModuleHandle(null), 0);
        if (handle == IntPtr.Zero) throw new Win32Exception();
        enabled = desiredEnabled;
    }
    private void Remove() { if (handle != IntPtr.Zero) Native.UnhookWindowsHookEx(handle); handle = IntPtr.Zero; }
    private void ResetState()
    {
        // Query asynchronous key state outside the low-level hook callback.
        int[] keys = { 0xA2, 0xA3, 0xA0, 0xA1, 0xA4, 0xA5, 0x5B, 0x5C, 0x43 };
        int snapshot = 0;
        for (int i = 0; i < keys.Length; i++) if ((Native.GetAsyncKeyState(keys[i]) & 0x8000) != 0) snapshot |= 1 << i;
        detector.Reset(snapshot);
    }
    private IntPtr OnKeyboard(int code, IntPtr message, IntPtr data)
    {
        try
        {
            if (code >= 0 && enabled)
            {
                var input = Marshal.PtrToStructure<Native.KeyboardData>(data);
                int kind = message.ToInt32();
                bool down = kind == 0x100 || kind == 0x104;
                detector.IntervalMs = IntervalMs;
                if (detector.Process((int)input.vkCode, (int)input.scanCode, down,
                    (input.flags & 0x20) != 0, false, false, Environment.TickCount64, Native.GetForegroundWindow()))
                {
                    // Never run UI, clipboard, disk IO or subscriber code inside the hook.
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try { if (enabled && Volatile.Read(ref disposed) == 0) DoubleCopy?.Invoke(); }
                        catch (InvalidOperationException) { }
                    });
                }
            }
        }
        catch { /* A keyboard hook must always preserve normal keyboard input. */ }
        return Native.CallNextHookEx(handle, code, message, data);
    }
    public void Restart() { if (Volatile.Read(ref disposed) == 0) Native.PostMessage(commandWindow, RestartMessage, IntPtr.Zero, IntPtr.Zero); }
    internal Task<long> PingAsync()
    {
        int id = Interlocked.Increment(ref pingSequence);
        var completion = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        pings[id] = (Stopwatch.GetTimestamp(), completion);
        if (!Native.PostMessage(commandWindow, PingMessage, new IntPtr(id), IntPtr.Zero))
        { pings.TryRemove(id, out _); completion.TrySetException(new Win32Exception()); }
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }
    private void Command(int message, IntPtr value)
    {
        if (message == ResetMessage) ResetState();
        else if (message == RestartMessage) { try { Install(); } catch { enabled = false; } }
        else if (message == StopMessage) { Remove(); Forms.Application.ExitThread(); }
        else if (message == PingMessage && pings.TryRemove(value.ToInt32(), out var ping))
            ping.Completion.TrySetResult((long)Stopwatch.GetElapsedTime(ping.At).TotalMilliseconds);
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        enabled = false;
        Native.PostMessage(commandWindow, StopMessage, IntPtr.Zero, IntPtr.Zero);
        if (worker.Join(2000)) ready.Dispose();
    }
    private sealed class CommandWindow : Forms.NativeWindow
    {
        private readonly KeyboardHook owner;
        public CommandWindow(KeyboardHook owner)
        {
            this.owner = owner;
            CreateHandle(new Forms.CreateParams { Caption = "QuickTranslate keyboard commands", Parent = new IntPtr(-3) });
        }
        protected override void WndProc(ref Forms.Message message)
        {
            if (message.Msg >= ResetMessage && message.Msg <= PingMessage) owner.Command(message.Msg, message.WParam);
            base.WndProc(ref message);
        }
        public void Release() => DestroyHandle();
    }
    private sealed class IOExceptionForHook : System.IO.IOException
    {
        public IOExceptionForHook(string message, Exception? inner = null) : base(message, inner) { }
    }
}
