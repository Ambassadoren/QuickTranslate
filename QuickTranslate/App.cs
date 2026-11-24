using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QuickTranslate;

internal static class App
{
    [STAThread]
    public static int Main(string[] args)
    {
        System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2);
        if (args.Length == 2 && args[0] == "--repair-data")
        {
            try { DataAccessRepair.GrantModify(PortablePaths.Root, args[1]); return 0; }
            catch (Exception ex) { MessageBox.Show("Не удалось разрешить запись в Data: " + ex.Message, "QuickTranslate", MessageBoxButton.OK, MessageBoxImage.Error); return 1; }
        }
        if (args.Length > 0 && args[0] == "--self-test") return SelfTest.Run(args.Length > 1 ? args[1] : "self-test.json");
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("Theme.xaml", UriKind.Relative) });
        if (args.Length > 1 && args[0] == "--preview") return RenderPreview(args[1]);
        if (args.Length > 1 && args[0] == "--preview-settings") return RenderWindow(new SettingsWindow(new AppSettings(), (_, _) => System.Threading.Tasks.Task.FromResult<string?>(null)), args[1], 540, 610);
        if (args.Length > 1 && args[0] == "--preview-popup")
        {
            var popup = new TranslationPopup();
            popup.SetWorking("EN → RU", "Перевожу…");
            popup.SetResult("Вот что мы узнали.");
            return RenderWindow(popup, args[1], (int)popup.Width, (int)popup.Height);
        }
        using var mutex = new Mutex(true, "Local\\QuickTranslate-Local-01", out bool first);
        if (!first)
        {
            var window = Native.FindWindow(null, MainWindow.WindowTitle);
            if (window != IntPtr.Zero) Native.PostMessage(window, 0x8005, IntPtr.Zero, IntPtr.Zero);
            else MessageBox.Show("Сначала завершите старую версию QuickTranslate: правая кнопка по её значку в трее → Выход. Затем запустите portable-версию.", "QuickTranslate", MessageBoxButton.OK, MessageBoxImage.Information);
            return 0;
        }
        application.DispatcherUnhandledException += (_, e) =>
        {
            MessageBox.Show(e.Exception.Message, "QuickTranslate", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
        var main = new MainWindow();
        application.MainWindow = main;
        if (args.Contains("--background"))
        {
            new WindowInteropHelper(main).EnsureHandle();
            application.Run();
        }
        else application.Run(main);
        return 0;
    }

    private static int RenderPreview(string path)
    {
        var window = new MainWindow(true);
        window.SourceText.Text = "The best way to predict the future is to create it.";
        window.ResultText.Text = "Лучший способ предсказать будущее — создать его.";
        window.ResultPlaceholder.Visibility = Visibility.Collapsed;
        window.TargetLabel.Text = "ПЕРЕВОД · РУССКИЙ";
        window.CopyButton.IsEnabled = true;
        window.StatusText.Text = "Готово · Перевод выполнен локально";
        return RenderWindow(window, path, 560, 440);
    }

    private static int RenderWindow(Window window, string path, int width, int height)
    {
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        var surface = new Border { Background = window.Background, Child = content, Width = width, Height = height };
        TextElement.SetForeground(surface, window.Foreground);
        TextElement.SetFontFamily(surface, window.FontFamily);
        TextElement.SetFontSize(surface, window.FontSize);
        surface.Measure(new Size(width, height));
        surface.Arrange(new Rect(0, 0, width, height));
        surface.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(surface);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var file = File.Create(path);
        encoder.Save(file);
        return 0;
    }
}
