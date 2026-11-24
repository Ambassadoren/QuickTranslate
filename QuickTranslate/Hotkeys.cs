using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace QuickTranslate;

internal sealed class DoubleCopyDetector
{
    private bool cDown;
    private int modifiers;
    private long lastCopy;
    private IntPtr lastWindow;
    public int IntervalMs { get; set; } = 650;
    public void Reset(int snapshot = 0)
    {
        modifiers = snapshot & 255;
        cDown = (snapshot & 256) != 0;
        lastCopy = 0;
        lastWindow = IntPtr.Zero;
    }
    public bool Process(int vk, int scan, bool down, bool alt, bool shift, bool win,
        long timestamp, IntPtr foreground)
    {
        int modifier = vk switch { 0x11 or 0xA2 => 1, 0xA3 => 2, 0x10 or 0xA0 => 4,
            0xA1 => 8, 0x12 or 0xA4 => 16, 0xA5 => 32, 0x5B => 64, 0x5C => 128, _ => 0 };
        if (modifier != 0)
        {
            if (down && (modifier & 3) != 0 && (modifiers & 3) == 0) cDown = false;
            modifiers = down ? modifiers | modifier : modifiers & ~modifier;
        }
        if (scan != 0x2E && vk != 0x43)
        {
            if (down && (modifier & 3) == 0) lastCopy = 0;
            return false;
        }
        if (!down) { cDown = false; return false; }
        if (cDown) return false;
        cDown = true;
        if ((modifiers & 3) == 0 || alt || shift || win || (modifiers & 252) != 0)
        { lastCopy = 0; return false; }
        bool fire = lastCopy > 0 && timestamp >= lastCopy && timestamp - lastCopy <= IntervalMs && foreground == lastWindow;
        lastCopy = fire ? 0 : timestamp;
        lastWindow = foreground;
        return fire;
    }
}

internal static class Native
{
    [DllImport("wtsapi32.dll")] internal static extern bool WTSRegisterSessionNotification(IntPtr window, uint flags);
    [DllImport("wtsapi32.dll")] internal static extern bool WTSUnRegisterSessionNotification(IntPtr window);
    internal delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] internal struct KeyboardData
    { public uint vkCode, scanCode, flags, time; public UIntPtr extra; }
    [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetWindowsHookEx(int id, HookProc callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(IntPtr handle);
    [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr handle, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindow(string? className, string title);
    [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}
