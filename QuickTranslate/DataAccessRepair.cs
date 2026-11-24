using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading.Tasks;

namespace QuickTranslate;

internal static class DataAccessRepair
{
    public static string Explain(Exception exception)
    {
        if (exception is UnauthorizedAccessException || (exception.HResult & 0xffff) == 5)
            return "Нет права записи в Data. Нажмите «Разрешить запись в Data» в настройках.";
        if ((exception.HResult & 0xffff) is 32 or 33)
            return "Файл настроек или истории занят. Закройте другую копию приложения и повторите попытку.";
        return "Не удалось сохранить данные: " + exception.Message;
    }
    internal static string ValidateDirectory(string root)
    {
        string fullRoot = Path.GetFullPath(root);
        string data = Path.GetFullPath(Path.Combine(fullRoot, "Data"));
        if (!string.Equals(Path.GetDirectoryName(data), fullRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Недопустимый путь Data.");
        // Avoid changing permissions outside this installation through a junction.
        var directory = new DirectoryInfo(fullRoot);
        while (directory != null)
        {
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Путь приложения содержит ссылку или точку подключения. Перенесите приложение в обычную папку с правом записи.");
            directory = directory.Parent;
        }
        if (Directory.Exists(data) && (File.GetAttributes(data) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Data является ссылкой. Разрешения автоматически не изменены.");
        return data;
    }
    internal static void GrantModify(string root, string userSid)
    {
        var identity = new SecurityIdentifier(userSid);
        if (!identity.IsAccountSid()) throw new IOException("Недопустимый пользователь.");
        string data = ValidateDirectory(root);
        Directory.CreateDirectory(data);
        var directory = new DirectoryInfo(data);
        var acl = directory.GetAccessControl(AccessControlSections.Access);
        acl.AddAccessRule(new FileSystemAccessRule(identity, FileSystemRights.Modify,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(acl);
    }
    public static async Task<string?> RequestAsync()
    {
        try
        {
            ValidateDirectory(PortablePaths.Root);
            string sid = WindowsIdentity.GetCurrent().User?.Value ?? throw new IOException("Не удалось определить пользователя Windows.");
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", WorkingDirectory = AppContext.BaseDirectory };
            start.ArgumentList.Add("--repair-data");
            start.ArgumentList.Add(sid);
            using var process = Process.Start(start) ?? throw new IOException("Не удалось запустить исправление прав.");
            await process.WaitForExitAsync();
            if (process.ExitCode != 0) return "Windows не разрешила изменить права Data. Перенесите приложение в папку, где у вас есть право записи.";
            // Verify access using the original, non-elevated process token.
            string probe = Path.Combine(PortablePaths.Data, ".write-check-" + Guid.NewGuid().ToString("N"));
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            return null;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { return "Подтверждение Windows отменено. Права не изменены."; }
        catch (Exception ex) { return Explain(ex); }
    }
}
