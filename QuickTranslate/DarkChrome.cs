using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace QuickTranslate;

internal static class DarkChrome
{
    public static void StyleTray(System.Windows.Forms.ContextMenuStrip menu)
    {
        menu.BackColor = System.Drawing.Color.FromArgb(29, 32, 42);
        menu.ForeColor = System.Drawing.Color.FromArgb(237, 240, 248);
        menu.Renderer = new System.Windows.Forms.ToolStripProfessionalRenderer(new DarkTrayColors());
    }
    public static void Attach(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            try
            {
                int dark = 1;
                DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 20, ref dark, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        };
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    private sealed class DarkTrayColors : System.Windows.Forms.ProfessionalColorTable
    {
        public override System.Drawing.Color ToolStripDropDownBackground => System.Drawing.Color.FromArgb(29, 32, 42);
        public override System.Drawing.Color ImageMarginGradientBegin => ToolStripDropDownBackground;
        public override System.Drawing.Color ImageMarginGradientMiddle => ToolStripDropDownBackground;
        public override System.Drawing.Color ImageMarginGradientEnd => ToolStripDropDownBackground;
        public override System.Drawing.Color MenuBorder => System.Drawing.Color.FromArgb(54, 59, 75);
        public override System.Drawing.Color MenuItemSelected => System.Drawing.Color.FromArgb(57, 48, 71);
        public override System.Drawing.Color MenuItemBorder => MenuItemSelected;
        public override System.Drawing.Color SeparatorDark => MenuBorder;
        public override System.Drawing.Color SeparatorLight => ToolStripDropDownBackground;
    }
}
