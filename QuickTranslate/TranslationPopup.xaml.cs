using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace QuickTranslate;

public partial class TranslationPopup : Window
{
    private bool exiting;
    private System.Drawing.Point anchor;
    public event Action? OpenEditorRequested;
    public TranslationPopup()
    {
        InitializeComponent();
        Closing += OnClosing;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Hide(); e.Handled = true; }
        };
    }

    public void ShowNearCursor()
    {
        anchor = Forms.Cursor.Position;
        Topmost = true;
        WindowState = WindowState.Normal;
        Show();
        PositionAtAnchor();
        // A monitor change can synchronously change the WPF DPI scale.
        PositionAtAnchor();
        Activate();
    }
    private void PositionAtAnchor()
    {
        if (!IsVisible) return;
        var handle = new WindowInteropHelper(this).Handle;
        var area = Forms.Screen.FromPoint(anchor).WorkingArea;
        double scale = Math.Max(96u, Native.GetDpiForWindow(handle)) / 96d;
        int width = (int)Math.Ceiling(Width * scale);
        int height = (int)Math.Ceiling(Height * scale);
        int x = Math.Clamp(anchor.X + 14, area.Left + 6, Math.Max(area.Left + 6, area.Right - width - 6));
        int candidateY = anchor.Y + 16;
        if (candidateY + height > area.Bottom - 6) candidateY = anchor.Y - height - 12;
        int y = Math.Clamp(candidateY, area.Top + 6, Math.Max(area.Top + 6, area.Bottom - height - 6));
        Native.SetWindowPos(handle, new IntPtr(-1), x, y, 0, 0, 0x41);
    }
    public void SetWorking(string direction, string message)
    {
        DirectionLabel.Text = direction;
        TranslationText.Text = message;
        TranslationText.Foreground = (Brush)FindResource("MutedBrush");
        PopupStatus.Text = "Локальная обработка…";
        CopyResultButton.IsEnabled = false;
        Height = 154;
    }
    public void SetResult(string result)
    {
        TranslationText.Text = result;
        TranslationText.Foreground = (Brush)FindResource("TextBrush");
        CopyResultButton.IsEnabled = !string.IsNullOrWhiteSpace(result);
        PopupStatus.Text = "Esc — скрыть";
        var measure = new TextBlock { Text = result, FontFamily = FontFamily, FontSize = 16, TextWrapping = TextWrapping.Wrap };
        measure.Measure(new Size(Width - 40, double.PositiveInfinity));
        Height = Math.Clamp(Math.Ceiling(measure.DesiredSize.Height) + 79, 138, 280);
        PositionAtAnchor();
    }
    public void SetError(string message)
    {
        TranslationText.Text = message;
        TranslationText.Foreground = (Brush)FindResource("ErrorBrush");
        CopyResultButton.IsEnabled = false;
        PopupStatus.Text = "Esc — скрыть · ↗ — открыть редактор";
        Height = 180;
        PositionAtAnchor();
    }
    public void SetStatus(string message) => PopupStatus.Text = message;
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (!CopyResultButton.IsEnabled) return;
        try { Clipboard.SetText(TranslationText.Text); PopupStatus.Text = "Перевод скопирован"; }
        catch { PopupStatus.Text = "Буфер занят. Попробуйте ещё раз."; }
    }
    private void Editor_Click(object sender, RoutedEventArgs e) { Hide(); OpenEditorRequested?.Invoke(); }
    private void Hide_Click(object sender, RoutedEventArgs e) => Hide();
    private void Header_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
    private void OnClosing(object? sender, CancelEventArgs e) { if (!exiting) { e.Cancel = true; Hide(); } }
    public void CloseForExit() { exiting = true; Close(); }
}
