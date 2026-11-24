using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

[assembly: AssemblyTitle("QuickTranslate")]
[assembly: AssemblyProduct("QuickTranslate Portable")]
[assembly: AssemblyVersion("0.3.1.0")]

internal static class Launcher
{
    [STAThread]
    private static int Main(string[] args)
    {
        string root = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        string app = Path.Combine(root, "app", "QuickTranslate.App.exe");
        if (!File.Exists(app))
        {
            MessageBox.Show("Распакуйте архив целиком. Рядом с QuickTranslate.exe должна быть папка app.", "QuickTranslate", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 1;
        }
        try
        {
            var arguments = new StringBuilder();
            foreach (string value in args) { if (arguments.Length > 0) arguments.Append(' '); arguments.Append(Quote(value)); }
            var start = new ProcessStartInfo(app, arguments.ToString())
            { WorkingDirectory = Path.GetDirectoryName(app), UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Normal };
            using (var process = Process.Start(start))
            {
                if (process == null) return 1;
                if (args.Length > 0 && (args[0] == "--self-test" || args[0] == "--preview"))
                { process.WaitForExit(); return process.ExitCode; }
            }
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Не удалось запустить приложение: " + ex.Message, "QuickTranslate", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
    private static string Quote(string value)
    {
        var result = new StringBuilder("\"");
        int slashes = 0;
        foreach (char ch in value)
        {
            if (ch == '\\') { slashes++; continue; }
            if (ch == '"') { result.Append('\\', slashes * 2 + 1); result.Append(ch); }
            else { result.Append('\\', slashes); result.Append(ch); }
            slashes = 0;
        }
        result.Append('\\', slashes * 2);
        return result.Append('"').ToString();
    }
}
