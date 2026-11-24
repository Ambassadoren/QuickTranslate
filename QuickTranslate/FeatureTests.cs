using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace QuickTranslate;

internal static class FeatureTests
{
    public static void Run(Action<string, Action> test, Action<bool, string> assert, string reportPath)
    {
        string fixtures = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath))!, "fixtures-" + Guid.NewGuid().ToString("N"));
        test("portable settings and custom shortcuts survive reload", () =>
        {
            string file = Path.Combine(fixtures, "settings.json");
            var settings = new AppSettings { DoubleCopy = false, TextShortcut = new ShortcutSpec(6, 0x51), CaptureShortcut = new ShortcutSpec(3, 0x77), HistoryEnabled = false };
            JsonFile.Write(file, settings);
            var actual = AppSettings.Load(file);
            assert(!actual.DoubleCopy && actual.TextShortcut == settings.TextShortcut && actual.CaptureShortcut == settings.CaptureShortcut && !actual.HistoryEnabled, "Settings were not retained");
            assert(PortablePaths.Data.StartsWith(PortablePaths.Root, StringComparison.OrdinalIgnoreCase), "Data is outside portable root");
        });
        test("history bounded, searchable, deduplicated and persistent", () =>
        {
            string file = Path.Combine(fixtures, "history.json");
            var store = new HistoryStore(file);
            for (int i = 0; i < 55; i++) store.Add("source-" + i, "перевод-" + i, "en", "ru");
            assert(store.Entries.Count == 50 && store.Entries[0].Source == "source-54", "History size/order is wrong");
            store.Add("source-53", "новый перевод", "en", "ru");
            assert(store.Entries.Count == 50 && store.Entries[0].Source == "source-53", "Duplicate entry not updated");
            assert(store.Search("НОВЫЙ").Count() == 1, "Translation search is not case-insensitive");
            var loaded = new HistoryStore(file);
            assert(loaded.Entries.Count == 50 && loaded.Entries[0].Translation == "новый перевод", "History reload failed");
            loaded.Clear();
            assert(new HistoryStore(file).Entries.Count == 0, "History was not cleared on disk");
        });
        test("shortcut validation and quoted background startup command", () =>
        {
            assert(new ShortcutSpec(3, 0x54).IsValid, "Valid shortcut rejected");
            assert(!new ShortcutSpec(0, 0x41).IsValid && !new ShortcutSpec(4, 0x41).IsValid && !new ShortcutSpec(2, 0xA2).IsValid, "Incomplete shortcut accepted");
            string launcher = Path.GetFullPath(Path.Combine(fixtures, "папка с пробелами", "QuickTranslate.exe"));
            assert(Autostart.CommandFor(launcher) == "\"" + launcher + "\" --background", "Startup path is not quoted");
        });
        test("compact window layout and expansion", () =>
        {
            var application = Application.Current ?? new Application();
            application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("Theme.xaml", UriKind.Relative) });
            var main = new MainWindow(true);
            assert(main.Width == 560 && main.Height == 470 && !main.ShowInTaskbar, "Default window is not compact");
            assert(((System.Windows.Media.SolidColorBrush)main.Foreground).Color.R > 200 && ((System.Windows.Media.SolidColorBrush)main.Background).Color.R < 40, "Editor dark theme is not inherited");
            main.ExpandButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            assert(main.Width == 900 && Grid.GetColumn(main.ResultPanel) == 2, "Expansion did not reflow editors");
            main.ExpandButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            assert(main.Width == 560 && Grid.GetRow(main.ResultPanel) == 2, "Collapse failed");
            var root = (FrameworkElement)main.Content;
            root.Measure(new Size(444, 360));
            root.Arrange(new Rect(0, 0, 444, 360));
            root.UpdateLayout();
            var bounds = main.TranslateButton.TransformToAncestor(root).TransformBounds(new Rect(main.TranslateButton.RenderSize));
            assert(bounds.Right <= 444 && bounds.Bottom <= 360 && main.ResultText.ActualHeight > 30, "Controls clip at minimum size");
        });
        test("history UI search and selected translation", () =>
        {
            var store = new HistoryStore(Path.Combine(fixtures, "history-ui.json"));
            store.Add("Hello", "Привет", "en", "ru");
            store.Add("Window", "Окно", "en", "ru");
            var dialog = new HistoryWindow(store, true);
            dialog.SearchBox.Text = "привет";
            assert(dialog.EntriesList.Items.Count == 1 && dialog.PreviewBox.Text.Contains("Hello"), "History UI did not filter/select the result");
        });
        test("dark topmost popup, long results and error state", () =>
        {
            var popup = new TranslationPopup();
            assert(popup.Width == 400 && popup.Height <= 180 && popup.Topmost && !popup.ShowInTaskbar, "Popup is not small and topmost");
            assert(popup.WindowStyle == WindowStyle.None && popup.AllowsTransparency, "Popup has a normal window frame");
            popup.SetWorking("EN → RU", "Перевожу…");
            assert(!popup.CopyResultButton.IsEnabled, "Copy enabled during loading");
            popup.SetResult("Привет, мир!");
            assert(popup.CopyResultButton.IsEnabled && popup.Height <= 180, "Short result is not compact");
            popup.SetResult(string.Join(" ", Enumerable.Repeat("Длинный перевод", 100)));
            assert(popup.Height <= 280 && popup.TranslationText.VerticalScrollBarVisibility == ScrollBarVisibility.Auto, "Long result grows without a bound");
            var handle = new System.Windows.Interop.WindowInteropHelper(popup).EnsureHandle();
            assert((Native.GetWindowLongPtr(handle, -20).ToInt64() & 8) != 0, "Native WS_EX_TOPMOST style is not set");
            popup.SetError("Проверка ошибки");
            assert(!popup.CopyResultButton.IsEnabled && popup.TranslationText.Text.Contains("ошибки"), "Error state can be copied");
            var color = ((System.Windows.Media.SolidColorBrush)popup.PopupSurface.Background).Color;
            assert(color.R < 40 && color.G < 40 && color.B < 40, "Popup background is not dark");
            popup.CloseForExit();
        });
        ReliabilityTests.Run(test, assert, fixtures);
    }
}
