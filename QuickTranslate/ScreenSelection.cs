using System;
using System.Drawing;
using System.Windows.Forms;

namespace QuickTranslate;

internal sealed class ScreenSelection : Form
{
    private readonly Bitmap screenshot;
    private Point start;
    private Rectangle selection;
    private bool selecting;
    public Bitmap? SelectedImage { get; private set; }

    public ScreenSelection()
    {
        var bounds = SystemInformation.VirtualScreen;
        screenshot = new Bitmap(bounds.Width, bounds.Height);
        using (var graphics = Graphics.FromImage(screenshot))
            graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        TopMost = true;
        ShowInTaskbar = false;
        DoubleBuffered = true;
        Cursor = Cursors.Cross;
        KeyPreview = true;
        Text = "Выделите область — Esc для отмены";
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.DrawImageUnscaled(screenshot, 0, 0);
        using var dim = new SolidBrush(Color.FromArgb(140, 17, 20, 32));
        e.Graphics.FillRectangle(dim, ClientRectangle);
        if (selection.Width > 0 && selection.Height > 0)
        {
            e.Graphics.DrawImage(screenshot, selection, selection, GraphicsUnit.Pixel);
            using var border = new Pen(Color.FromArgb(167, 139, 250), 2);
            e.Graphics.DrawRectangle(border, selection);
        }
        using var font = new Font("Segoe UI", 13);
        using var brush = new SolidBrush(Color.White);
        e.Graphics.DrawString("Выделите текст на экране   ·   Esc — отмена", font, brush, 30, 24);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right) { Close(); return; }
        if (e.Button != MouseButtons.Left) return;
        start = e.Location;
        selecting = true;
        Capture = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!selecting) return;
        selection = Rectangle.Intersect(ClientRectangle, Rectangle.FromLTRB(
            Math.Min(start.X, e.X), Math.Min(start.Y, e.Y), Math.Max(start.X, e.X), Math.Max(start.Y, e.Y)));
        Invalidate();
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (!selecting || e.Button != MouseButtons.Left) return;
        selecting = false;
        Capture = false;
        if (selection.Width >= 8 && selection.Height >= 8)
            SelectedImage = screenshot.Clone(selection, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        Close();
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) Close();
        base.OnKeyDown(e);
    }
    protected override void Dispose(bool disposing)
    { if (disposing) screenshot.Dispose(); base.Dispose(disposing); }
}
