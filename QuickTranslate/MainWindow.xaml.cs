using System;
using System.ComponentModel;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Tesseract;
using Forms = System.Windows.Forms;

namespace QuickTranslate;

public partial class MainWindow : Window
{
    internal const string WindowTitle = "QuickTranslate — Portable";
    private readonly EngineClient engine = new();
    private ShortcutManager? shortcuts;
    private Forms.NotifyIcon? tray;
    private TranslationPopup? popup;
    private IntPtr nativeWindow;
    private bool exiting, initialized, capturing, expanded, auxiliaryVisible;
    private readonly bool preview;
    private AppSettings settings = new();
    private HistoryStore history = new(PortablePaths.History, false);
    private string startupWarning = "";
    private long revision;
    private CancellationTokenSource? pending;
    private readonly SemaphoreSlim settingsWriteGate = new(1, 1);

    public MainWindow(bool preview = false)
    {
        this.preview = preview;
        if (!preview)
        {
            try { settings = AppSettings.Load(PortablePaths.Settings); }
            catch { startupWarning = "Настройки не прочитаны. Используются стандартные значения."; }
            try { history = new HistoryStore(PortablePaths.History); }
            catch { settings.HistoryEnabled = false; startupWarning = "История не прочитана. Новые записи отключены; исходный файл сохранён в Data."; }
        }
        InitializeComponent();
        DarkChrome.Attach(this);
        Title = WindowTitle;
        SourceInitialized += OnSourceInitialized;
        Closing += OnClosing;
        PreviewKeyDown += OnKey;
        DirectionBox.SelectedIndex = settings.Direction;
        initialized = true;
        UpdateShortcutHints();
        Loaded += (_, _) => { if (!preview) PlaceNearCursor(); };
        StateChanged += (_, _) => { if (WindowState == WindowState.Maximized) { WindowState = WindowState.Normal; SetExpanded(true); } };
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (preview) return;
        nativeWindow = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(nativeWindow)?.AddHook(WindowMessages);
        Native.WTSRegisterSessionNotification(nativeWindow, 0);
        shortcuts = new ShortcutManager(nativeWindow);
        shortcuts.TranslateClipboard += () => Dispatcher.BeginInvoke(new Action(async () =>
            {
                if (auxiliaryVisible || capturing) return;
                await Task.Delay(160);
                await ReadClipboardAsync();
            }));
        shortcuts.Capture += () => Dispatcher.BeginInvoke(new Action(async () => { if (!auxiliaryVisible) await CaptureAsync(); }));
        shortcuts.TryApply(settings, true, out string warning);
        if (warning.Length > 0) startupWarning = warning;
        var menu = new Forms.ContextMenuStrip();
        DarkChrome.StyleTray(menu);
        menu.Items.Add("Открыть QuickTranslate", null, (_, _) => Dispatcher.Invoke(() => Reveal()));
        menu.Items.Add("Перевести область экрана", null, (_, _) => Dispatcher.Invoke(async () => await CaptureAsync()));
        menu.Items.Add("История", null, (_, _) => Dispatcher.Invoke(() => ShowHistory()));
        menu.Items.Add("Настройки", null, (_, _) => Dispatcher.Invoke(() => ShowSettings()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => Dispatcher.Invoke(Exit));
        tray = new Forms.NotifyIcon
        {
            Text = "QuickTranslate", Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
            Visible = true, ContextMenuStrip = menu,
        };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(() => Reveal());
        UpdateShortcutHints();
        if (startupWarning.Length > 0) SetStatus(startupWarning, true);
    }

    private IntPtr WindowMessages(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x2B1 && wParam.ToInt32() is 5 or 7 || message == 0x218 && wParam.ToInt32() is 7 or 0x12)
            shortcuts?.RestartKeyboard();
        if (message == 0x312)
        { shortcuts?.HandleMessage(wParam.ToInt32()); handled = true; }
        if (message == 0x8005) { Reveal(); handled = true; }
        return IntPtr.Zero;
    }

    private void Reveal(bool nearCursor = false)
    {
        if (auxiliaryVisible) return;
        popup?.Hide();
        SetExpanded(false);
        WindowState = WindowState.Normal;
        Show();
        if (nearCursor) PlaceNearCursor();
        Activate();
    }
    private TranslationPopup Popup
    {
        get
        {
            if (popup == null)
            {
                popup = new TranslationPopup();
                popup.OpenEditorRequested += () => { Reveal(); SetExpanded(true); PlaceNearCursor(); };
            }
            return popup;
        }
    }
    private void PresentPopup(string direction, string message)
    {
        Hide();
        Popup.SetWorking(direction, message);
        Popup.ShowNearCursor();
    }
    private void PresentProblem(string message, bool usePopup)
    {
        SetStatus(message, true);
        if (usePopup)
        {
            Hide();
            Popup.SetError(message);
            Popup.ShowNearCursor();
        }
        else Reveal();
    }
    private void PlaceNearCursor()
    {
        if (preview) return;
        var cursor = Forms.Cursor.Position;
        var area = Forms.Screen.FromPoint(cursor).WorkingArea;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        double scale = Math.Max(1, Native.GetDpiForWindow(handle)) / 96d;
        int width = (int)Math.Ceiling(Width * scale);
        int height = (int)Math.Ceiling(Height * scale);
        int left = Math.Clamp(cursor.X + 18, area.Left + 8, Math.Max(area.Left + 8, area.Right - width - 8));
        int top = Math.Clamp(cursor.Y + 18, area.Top + 8, Math.Max(area.Top + 8, area.Bottom - height - 8));
        Native.SetWindowPos(handle, IntPtr.Zero, left, top, 0, 0, 0x15);
    }
    private void SetExpanded(bool value)
    {
        expanded = value;
        Width = value ? 900 : 560;
        Height = value ? 590 : 470;
        ExpandButton.Content = value ? "Свернуть" : "Развернуть";
        EditorColumnGap.Width = new GridLength(value ? 12 : 0);
        EditorColumn2.Width = value ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        SourceRow.Height = value ? new GridLength(1, GridUnitType.Star) : new GridLength(88);
        EditorRowGap.Height = new GridLength(value ? 0 : 10);
        ResultRow.Height = value ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(ResultPanel, value ? 2 : 0);
        Grid.SetRow(ResultPanel, value ? 0 : 2);
    }
    private void Expand_Click(object sender, RoutedEventArgs e) { SetExpanded(!expanded); PlaceNearCursor(); }
    private void Exit() { exiting = true; Close(); Application.Current.Shutdown(); }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!exiting) { e.Cancel = true; Hide(); return; }
        pending?.Cancel();
        shortcuts?.Dispose();
        Native.WTSUnRegisterSessionNotification(nativeWindow);
        tray?.Dispose();
        popup?.CloseForExit();
        engine.Dispose();
    }
    private async void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Hide(); e.Handled = true; }
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        { e.Handled = true; await TranslateCurrentAsync(); }
    }

    private void SourceText_Changed(object sender, TextChangedEventArgs e)
    {
        if (!initialized) return;
        revision++;
        pending?.Cancel();
        SourcePlaceholder.Visibility = string.IsNullOrEmpty(SourceText.Text) ? Visibility.Visible : Visibility.Collapsed;
        CharacterCount.Text = $"{SourceText.Text.Length:N0} / 20 000";
        ResultText.Clear();
        ResultPlaceholder.Visibility = Visibility.Visible;
        CopyButton.IsEnabled = false;
    }
    private async void Direction_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!initialized) return;
        revision++;
        pending?.Cancel();
        settings.Direction = DirectionBox.SelectedIndex;
        ResultText.Clear();
        ResultPlaceholder.Visibility = Visibility.Visible;
        CopyButton.IsEnabled = false;
        if (!preview) try { await WriteSettingsAsync(settings.Clone()); } catch (Exception ex) { SetStatus(DataAccessRepair.Explain(ex), true); }
    }
    private async void Translate_Click(object sender, RoutedEventArgs e) => await TranslateCurrentAsync();
    private async Task TranslateCurrentAsync(bool usePopup = false)
    {
        string input = SourceText.Text.Trim();
        if (input.Length == 0) { PresentProblem("Добавьте текст для перевода.", usePopup); return; }
        if (input.Length > 20000) { PresentProblem("Максимум 20 000 символов за один перевод.", usePopup); return; }
        pending?.Cancel();
        var cancellation = new CancellationTokenSource();
        pending = cancellation;
        long current = ++revision;
        int direction = DirectionBox.SelectedIndex;
        int russian = Regex.Matches(input, "[А-Яа-яЁё]").Count;
        int english = Regex.Matches(input, "[A-Za-z]").Count;
        string source = direction == 2 || direction == 0 && russian > english ? "ru" : "en";
        string target = source == "en" ? "ru" : "en";
        TargetLabel.Text = target == "ru" ? "ПЕРЕВОД · РУССКИЙ" : "ПЕРЕВОД · ENGLISH";
        if (usePopup) PresentPopup(source.ToUpperInvariant() + " → " + target.ToUpperInvariant(), "Перевожу…");
        TranslateButton.IsEnabled = false;
        SetStatus("Перевожу на компьютере… Первый запуск модели может занять несколько секунд.");
        try
        {
            string output = await engine.TranslateAsync(input, source, target, cancellation.Token);
            if (current != revision) return;
            ResultText.Text = output;
            ResultPlaceholder.Visibility = output.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
            CopyButton.IsEnabled = output.Length > 0;
            if (usePopup) Popup.SetResult(output);
            SetStatus("Готово · Перевод выполнен локально");
            if (settings.HistoryEnabled && !preview)
            {
                try { await history.AddAsync(input, output, source, target); }
                catch (Exception ex)
                {
                    if (current == revision)
                    {
                        SetStatus("Перевод готов. " + DataAccessRepair.Explain(ex), true);
                        if (usePopup) Popup.SetStatus("Ошибка записи Data — откройте настройки");
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (current == revision)
            {
                SetStatus(ex.Message, true);
                if (usePopup) Popup.SetError(ex.Message);
            }
        }
        finally
        {
            if (ReferenceEquals(pending, cancellation))
            {
                TranslateButton.IsEnabled = true;
                pending = null;
            }
            cancellation.Dispose();
        }
    }

    private async void Paste_Click(object sender, RoutedEventArgs e) => await ReadClipboardAsync(false);
    private async Task ReadClipboardAsync(bool usePopup = true)
    {
        if (capturing) return;
        if (usePopup)
        {
            PresentPopup("БУФЕР ОБМЕНА", "Читаю текст…");
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
        }
        for (int attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                if (Clipboard.ContainsImage())
                {
                    var image = Clipboard.GetImage();
                    if (image == null) break;
                    using var memory = new MemoryStream();
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(image));
                    encoder.Save(memory);
                    if (!usePopup) Reveal(true);
                    await RecognizeAsync(memory.ToArray(), usePopup);
                    return;
                }
                if (Clipboard.ContainsText())
                {
                    string text = Clipboard.GetText();
                    if (!usePopup) Reveal(true);
                    if (text.Length > 20000) { PresentProblem("В буфере больше 20 000 символов. Выберите меньший фрагмент.", usePopup); return; }
                    SourceText.Text = text;
                    await TranslateCurrentAsync(usePopup);
                    return;
                }
                PresentProblem("В буфере нет текста или изображения.", usePopup); return;
            }
            catch (System.Runtime.InteropServices.COMException) { await Task.Delay(70); }
        }
        PresentProblem("Буфер обмена занят. Повторите копирование.", usePopup);
    }

    private async void Image_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Выберите фото с текстом", Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 40_000_000) throw new IOException("Выберите изображение меньше 40 МБ.");
            await RecognizeAsync(await File.ReadAllBytesAsync(dialog.FileName));
        }
        catch (Exception ex) { SetStatus("Не удалось открыть фото: " + ex.Message, true); }
    }
    private async void Capture_Click(object sender, RoutedEventArgs e) => await CaptureAsync();
    private async Task CaptureAsync()
    {
        if (capturing) return;
        capturing = true;
        popup?.Hide();
        Hide();
        try
        {
            await Task.Delay(220);
            using var selection = new ScreenSelection();
            selection.ShowDialog();
            using var bitmap = selection.SelectedImage;
            if (bitmap == null) { SetStatus("Выделение отменено."); return; }
            using var memory = new MemoryStream();
            bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
            await RecognizeAsync(memory.ToArray(), true);
        }
        catch (Exception ex) { Reveal(); SetStatus("Не удалось захватить область: " + ex.Message, true); }
        finally { capturing = false; }
    }
    private async Task RecognizeAsync(byte[] image, bool usePopup = false)
    {
        pending?.Cancel();
        long current = ++revision;
        SetStatus("Распознаю текст на изображении…");
        if (usePopup) PresentPopup("ИЗОБРАЖЕНИЕ", "Распознаю текст…");
        try
        {
            string text = await Task.Run(() => OcrImage(image));
            if (current != revision) return;
            if (string.IsNullOrWhiteSpace(text)) { PresentProblem("Текст не найден. Попробуйте более крупный и чёткий фрагмент.", usePopup); return; }
            if (text.Length > 20000) { PresentProblem("На изображении больше 20 000 символов. Выделите меньшую область.", usePopup); return; }
            SourceText.Text = text.Trim();
            await TranslateCurrentAsync(usePopup);
        }
        catch (Exception ex) { if (current == revision) PresentProblem("Ошибка распознавания: " + ex.Message, usePopup); }
    }
    internal static string OcrImage(byte[] image)
    {
        using var ocr = new TesseractEngine(Path.Combine(AppContext.BaseDirectory, "tessdata"), "eng+rus", EngineMode.LstmOnly);
        using var pix = Pix.LoadFromMemory(image);
        using var page = ocr.Process(pix, PageSegMode.Auto);
        return page.GetText();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(ResultText.Text); SetStatus("Перевод скопирован."); }
        catch { SetStatus("Буфер обмена занят. Попробуйте ещё раз.", true); }
    }
    private void Swap_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ResultText.Text)) return;
        string next = ResultText.Text;
        DirectionBox.SelectedIndex = TargetLabel.Text.Contains("РУССКИЙ") ? 2 : 1;
        SourceText.Text = next;
        SetStatus("Текст перенесён. Нажмите «Перевести».");
    }
    private void Settings_Click(object sender, RoutedEventArgs e) => ShowSettings();
    private void ShowSettings()
    {
        if (auxiliaryVisible) return;
        Reveal();
        auxiliaryVisible = true;
        if (shortcuts != null) shortcuts.Suspended = true;
        try
        {
            var dialog = new SettingsWindow(settings, SaveSettings) { Owner = this, Icon = Icon };
            if (dialog.ShowDialog() == true) SetStatus("Настройки сохранены. " + ShortcutHint());
        }
        finally { auxiliaryVisible = false; if (shortcuts != null) shortcuts.Suspended = false; }
    }
    private async Task WriteSettingsAsync(AppSettings value)
    {
        await settingsWriteGate.WaitAsync();
        try { await Task.Run(() => JsonFile.Write(PortablePaths.Settings, value)); }
        finally { settingsWriteGate.Release(); }
    }
    private async Task<string?> SaveSettings(AppSettings next, bool? startup)
    {
        var previous = settings.Clone();
        bool changingStartup = false;
        if (shortcuts != null && !shortcuts.TryApply(next, false, out string warning)) return warning;
        try
        {
            await WriteSettingsAsync(next);
            if (startup.HasValue)
            {
                changingStartup = true;
                await Task.Run(() => Autostart.SetEnabled(startup.Value));
                changingStartup = false;
            }
            settings = next;
            UpdateShortcutHints();
            return null;
        }
        catch (Exception ex)
        {
            shortcuts?.TryApply(previous, true, out _);
            try { await WriteSettingsAsync(previous); } catch { }
            return changingStartup ? "Не удалось изменить автозапуск Windows: " + ex.Message : "Настройки не применены. " + DataAccessRepair.Explain(ex);
        }
    }
    private string ShortcutHint() => (settings.TextEnabled ? settings.DoubleCopy ? "Ctrl+C+C" : settings.TextShortcut.ToString() : "Перевод клавишей выключен") + " · Esc — скрыть";
    private void UpdateShortcutHints()
    {
        string text = settings.TextEnabled ? settings.DoubleCopy ? "Ctrl+C+C" : settings.TextShortcut.ToString() : "Вставить";
        SourcePlaceholder.Text = settings.DoubleCopy && settings.TextEnabled ? "Выделите текст и нажмите " + text : "Вставьте текст или вызовите перевод буфера";
        CaptureButton.ToolTip = settings.CaptureEnabled ? settings.CaptureShortcut.ToString() : "Выделить область экрана";
        if (tray != null) tray.Text = "QuickTranslate · " + text;
        SetStatus(ShortcutHint());
    }
    private void History_Click(object sender, RoutedEventArgs e) => ShowHistory();
    private void ShowHistory()
    {
        if (auxiliaryVisible) return;
        Reveal();
        auxiliaryVisible = true;
        if (shortcuts != null) shortcuts.Suspended = true;
        try
        {
            var dialog = new HistoryWindow(history, settings.HistoryEnabled) { Owner = this, Icon = Icon };
            if (dialog.ShowDialog() == true && dialog.Selected is HistoryEntry entry)
            {
                DirectionBox.SelectedIndex = entry.SourceLanguage == "ru" ? 2 : 1;
                SourceText.Text = entry.Source;
                ResultText.Text = entry.Translation;
                ResultPlaceholder.Visibility = Visibility.Collapsed;
                CopyButton.IsEnabled = true;
                TargetLabel.Text = entry.TargetLanguage == "ru" ? "ПЕРЕВОД · РУССКИЙ" : "ПЕРЕВОД · ENGLISH";
                SetStatus("Открыт перевод из истории");
            }
        }
        finally { auxiliaryVisible = false; if (shortcuts != null) shortcuts.Suspended = false; }
    }
    private void SetStatus(string text, bool error = false)
    { StatusText.Text = text; StatusText.Foreground = new SolidColorBrush(error ? Color.FromRgb(243, 151, 166) : Color.FromRgb(156, 166, 188)); }
}
