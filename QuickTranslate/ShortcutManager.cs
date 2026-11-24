using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace QuickTranslate;

internal sealed record ShortcutSpec(uint Modifiers, uint Key)
{
    public bool IsValid => (Modifiers & ~7u) == 0 && (Modifiers & 3) != 0 && Key >= 8 && Key <= 254
        && Key is not (0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5);
    public override string ToString()
    {
        var parts = new List<string>();
        if ((Modifiers & 2) != 0) parts.Add("Ctrl");
        if ((Modifiers & 1) != 0) parts.Add("Alt");
        if ((Modifiers & 4) != 0) parts.Add("Shift");
        parts.Add(Key == 0x20 ? "Space" : Key == 0x0D ? "Enter" : KeyInterop.KeyFromVirtualKey((int)Key).ToString());
        return string.Join("+", parts);
    }
}

internal sealed class ShortcutManager : IDisposable
{
    private readonly IntPtr window;
    private KeyboardHook? keyboard;
    private AppSettings current = new() { TextEnabled = false, CaptureEnabled = false };
    public event Action? TranslateClipboard;
    public event Action? Capture;
    private bool suspended;
    public bool Suspended
    {
        get => suspended;
        set
        {
            if (suspended == value) return;
            suspended = value;
            if (value) ReleaseBindings(); else ApplyCore(current.Clone(), true);
        }
    }
    public ShortcutManager(IntPtr window) { this.window = window; }
    public bool TryApply(AppSettings settings, bool allowPartial, out string warning)
    {
        if (settings.TextEnabled && !settings.DoubleCopy && !settings.TextShortcut.IsValid || settings.CaptureEnabled && !settings.CaptureShortcut.IsValid)
        { warning = "Выберите сочетание с Ctrl или Alt и обычной клавишей."; return false; }
        if (settings.TextEnabled && !settings.DoubleCopy && settings.CaptureEnabled && settings.TextShortcut == settings.CaptureShortcut)
        { warning = "Для текста и области экрана нужны разные сочетания."; return false; }
        var previous = current.Clone();
        try { warning = ApplyCore(settings, allowPartial); if (suspended) ReleaseBindings(); return true; }
        catch (Exception ex)
        {
            try { ApplyCore(previous, true); if (suspended) ReleaseBindings(); } catch { }
            warning = ex.Message;
            return false;
        }
    }
    private string ApplyCore(AppSettings settings, bool allowPartial)
    {
        ReleaseBindings();
        current = settings.Clone();
        var warnings = new List<string>();
        void Failure(string message, bool text)
        {
            if (!allowPartial) throw new InvalidOperationException(message);
            warnings.Add(message);
            if (text) current.TextEnabled = false; else current.CaptureEnabled = false;
        }
        if (settings.TextEnabled && settings.DoubleCopy)
        {
            try
            {
                if (keyboard == null)
                {
                    keyboard = new KeyboardHook();
                    keyboard.DoubleCopy += () => { if (!Suspended) TranslateClipboard?.Invoke(); };
                }
                keyboard.IntervalMs = settings.DoubleCopyInterval;
                keyboard.Enabled = true;
            }
            catch { Failure("Не удалось включить Ctrl+C+C. Доступна кнопка «Вставить».", true); }
        }
        else if (settings.TextEnabled && !Native.RegisterHotKey(window, 1, settings.TextShortcut.Modifiers | 0x4000, settings.TextShortcut.Key))
            Failure($"Сочетание {settings.TextShortcut} занято или недоступно. Выберите другое в настройках.", true);
        if (settings.CaptureEnabled && !Native.RegisterHotKey(window, 2, settings.CaptureShortcut.Modifiers | 0x4000, settings.CaptureShortcut.Key))
            Failure($"Сочетание {settings.CaptureShortcut} занято или недоступно. Выберите другое в настройках.", false);
        return string.Join(" ", warnings);
    }
    public void HandleMessage(int id)
    {
        if (Suspended) return;
        if (id == 1) TranslateClipboard?.Invoke();
        if (id == 2) Capture?.Invoke();
    }
    public void RestartKeyboard() => keyboard?.Restart();
    public void Dispose()
    {
        ReleaseBindings();
        keyboard?.Dispose();
    }
    private void ReleaseBindings()
    {
        Native.UnregisterHotKey(window, 1);
        Native.UnregisterHotKey(window, 2);
        if (keyboard != null) keyboard.Enabled = false;
    }
}
