using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace QuickTranslate;

public sealed class ShortcutBox : TextBox
{
    internal ShortcutSpec Shortcut { get; private set; } = new(3, 0x54);
    public ShortcutBox()
    {
        IsReadOnly = true;
        MinHeight = 36;
        VerticalContentAlignment = VerticalAlignment.Center;
        Cursor = Cursors.Hand;
        ToolTip = "Щёлкните и нажмите новое сочетание";
    }
    internal void SetShortcut(ShortcutSpec value) { Shortcut = value; Text = value.ToString(); }
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Tab) { base.OnPreviewKeyDown(e); return; }
        if (key == Key.Escape) { Keyboard.ClearFocus(); e.Handled = true; return; }
        e.Handled = true;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        var shortcut = new ShortcutSpec((uint)Keyboard.Modifiers, (uint)KeyInterop.VirtualKeyFromKey(key));
        if (!shortcut.IsValid) { ToolTip = "Используйте Ctrl или Alt вместе с обычной клавишей."; return; }
        SetShortcut(shortcut);
        ToolTip = "Сочетание будет проверено при сохранении.";
    }
}

public partial class SettingsWindow : Window
{
    private readonly AppSettings original;
    private readonly Func<AppSettings, bool?, Task<string?>> save;
    private bool busy;
    private bool initialized;
    private bool? originalStartup;
    internal SettingsWindow(AppSettings settings, Func<AppSettings, bool?, Task<string?>> save)
    {
        InitializeComponent();
        DarkChrome.Attach(this);
        original = settings.Clone();
        this.save = save;
        TextEnabledBox.IsChecked = settings.TextEnabled;
        TextModeBox.SelectedIndex = settings.DoubleCopy ? 0 : 1;
        IntervalBox.SelectedIndex = settings.DoubleCopyInterval <= 350 ? 0 : settings.DoubleCopyInterval >= 1000 ? 2 : 1;
        TextShortcutBox.SetShortcut(settings.TextShortcut);
        CaptureEnabledBox.IsChecked = settings.CaptureEnabled;
        CaptureShortcutBox.SetShortcut(settings.CaptureShortcut);
        HistoryEnabledBox.IsChecked = settings.HistoryEnabled;
        try { originalStartup = Autostart.IsEnabled(); StartupBox.IsChecked = originalStartup; }
        catch { StartupBox.IsEnabled = false; StartupNote.Text = "Windows не разрешила прочитать настройки автозапуска. Остальные настройки доступны."; }
        initialized = true;
        UpdateMode();
        MaxHeight = Math.Max(400, SystemParameters.WorkArea.Height - 30);
        Closing += (_, e) => { if (busy) e.Cancel = true; };
    }
    private void TextMode_Changed(object sender, SelectionChangedEventArgs e) { if (initialized) UpdateMode(); }
    private void UpdateMode()
    {
        bool doubleCopy = TextModeBox.SelectedIndex == 0;
        IntervalPanel.Visibility = doubleCopy ? Visibility.Visible : Visibility.Collapsed;
        CustomTextPanel.Visibility = doubleCopy ? Visibility.Collapsed : Visibility.Visible;
    }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var next = original.Clone();
        next.TextEnabled = TextEnabledBox.IsChecked == true;
        next.DoubleCopy = TextModeBox.SelectedIndex == 0;
        next.DoubleCopyInterval = IntervalBox.SelectedIndex == 0 ? 350 : IntervalBox.SelectedIndex == 2 ? 1000 : 650;
        next.TextShortcut = TextShortcutBox.Shortcut;
        next.CaptureEnabled = CaptureEnabledBox.IsChecked == true;
        next.CaptureShortcut = CaptureShortcutBox.Shortcut;
        next.HistoryEnabled = HistoryEnabledBox.IsChecked == true;
        bool? startup = originalStartup.HasValue && StartupBox.IsChecked != originalStartup ? StartupBox.IsChecked == true : null;
        SetBusy(true);
        string? error;
        try { error = await save(next, startup); }
        catch (Exception ex) { error = DataAccessRepair.Explain(ex); }
        finally { SetBusy(false); }
        if (error != null) { ErrorText.Foreground = (System.Windows.Media.Brush)FindResource("ErrorBrush"); ErrorText.Text = error; return; }
        DialogResult = true;
    }
    private async void RepairData_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        SetBusy(true);
        ErrorText.Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush");
        ErrorText.Text = "Подтвердите запрос Windows. Разрешение изменится только для вашей папки Data.";
        string? error;
        try { error = await DataAccessRepair.RequestAsync(); }
        finally { SetBusy(false); }
        ErrorText.Foreground = (System.Windows.Media.Brush)FindResource(error == null ? "TextBrush" : "ErrorBrush");
        ErrorText.Text = error ?? "Запись в Data разрешена. Теперь нажмите «Сохранить».";
    }
    private void SetBusy(bool value)
    {
        busy = value;
        SaveButton.IsEnabled = CancelButton.IsEnabled = RepairDataButton.IsEnabled = !value;
        SaveButton.Content = value ? "Подождите…" : "Сохранить";
    }
}
