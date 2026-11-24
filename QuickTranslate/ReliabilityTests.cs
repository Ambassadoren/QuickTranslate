using System;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;

namespace QuickTranslate;

internal static class ReliabilityTests
{
    public static void Run(Action<string, Action> test, Action<bool, string> assert, string fixtures)
    {
        test("keyboard message pump survives a blocked caller thread", () =>
        {
            using var hook = new KeyboardHook { Enabled = false };
            assert(hook.WorkerThreadId != Environment.CurrentManagedThreadId, "Hook still runs on the caller/UI thread");
            var ping = hook.PingAsync();
            Thread.Sleep(1250); // Simulate a UI freeze beyond LowLevelHooksTimeout.
            assert(ping.IsCompletedSuccessfully && ping.Result < 500, "Keyboard thread was blocked by the UI thread");
            hook.Restart();
            assert(hook.PingAsync().GetAwaiter().GetResult() < 500, "Keyboard thread did not recover after restart");
            assert(!hook.Enabled, "Restart enabled a disabled hook");
            hook.Enabled = true;
            hook.Restart();
            hook.PingAsync().GetAwaiter().GetResult();
            assert(hook.Enabled, "Restart lost the requested enabled state");
        });
        test("modifiers and lost key-up recovery", () =>
        {
            var d = new DoubleCopyDetector();
            var window = new IntPtr(123);
            d.Reset(1); // Ctrl was held before the hook was enabled.
            assert(!d.Process(0x43, 0x2E, true, false, false, false, 100, window), "First C fired");
            d.Process(0x43, 0x2E, false, false, false, false, 110, window);
            assert(d.Process(0x43, 0x2E, true, false, false, false, 200, window), "Held Ctrl at startup was not recognized");
            d.Reset();
            d.Process(0xA2, 0x1D, true, false, false, false, 300, window);
            d.Process(0xA0, 0x2A, true, false, false, false, 310, window);
            assert(!d.Process(0x43, 0x2E, true, false, false, false, 320, window), "Ctrl+Shift+C was treated as normal copy");
            d.Reset();
            d.Process(0xA2, 0x1D, true, false, false, false, 500, window);
            d.Process(0x43, 0x2E, true, false, false, false, 510, window);
            // A key-up may be lost across a desktop switch. A fresh Ctrl press recovers.
            d.Process(0xA2, 0x1D, false, false, false, false, 520, window);
            d.Process(0xA2, 0x1D, true, false, false, false, 550, window);
            assert(d.Process(0x43, 0x2E, true, false, false, false, 560, window), "A lost C key-up left the detector stuck");
        });
        test("stale read-only temp file does not block JSON save", () =>
        {
            string file = Path.Combine(fixtures, "stale-temp.json");
            Directory.CreateDirectory(fixtures);
            string stale = file + ".tmp";
            File.WriteAllText(stale, "preserve this file");
            File.SetAttributes(stale, FileAttributes.ReadOnly);
            try
            {
                JsonFile.Write(file, new { Value = "saved" });
                assert(File.ReadAllText(file).Contains("saved"), "Settings not saved");
                assert(File.ReadAllText(stale) == "preserve this file", "Existing temp file was modified");
            }
            finally { File.SetAttributes(stale, FileAttributes.Normal); }
        });
        test("failed replacement preserves the original JSON", () =>
        {
            string file = Path.Combine(fixtures, "protected-settings.json");
            File.WriteAllText(file, "original settings");
            File.SetAttributes(file, FileAttributes.ReadOnly);
            try
            {
                bool failed = false;
                try { JsonFile.Write(file, new { Value = "replacement" }); }
                catch (UnauthorizedAccessException) { failed = true; }
                catch (IOException) { failed = true; }
                assert(failed && File.ReadAllText(file) == "original settings", "Original data was lost after a failed save");
                assert(Directory.GetFiles(fixtures, "protected-settings.json.*.tmp").Length == 0, "Failed save left partial temp files");
            }
            finally { File.SetAttributes(file, FileAttributes.Normal); }
        });
        test("data access repair is limited to Data and preserves the parent ACL", () =>
        {
            string root = Path.Combine(fixtures, "repair-fixture");
            Directory.CreateDirectory(root);
            string parentAcl = new DirectoryInfo(root).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access);
            var sid = WindowsIdentity.GetCurrent().User!;
            DataAccessRepair.GrantModify(root, sid.Value);
            assert(new DirectoryInfo(root).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access) == parentAcl, "Repair modified the application root ACL");
            var rules = new DirectoryInfo(Path.Combine(root, "Data")).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier));
            assert(rules.Cast<FileSystemAccessRule>().Any(r => r.IdentityReference.Equals(sid) && r.AccessControlType == AccessControlType.Allow && (r.FileSystemRights & FileSystemRights.Modify) == FileSystemRights.Modify), "User did not receive Modify access to Data");
        });
    }
}
