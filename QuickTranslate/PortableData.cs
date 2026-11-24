using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace QuickTranslate;

internal static class PortablePaths
{
    public static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
    public static string Launcher => Path.Combine(Root, "QuickTranslate.exe");
    public static string Data => Path.Combine(Root, "Data");
    public static string Settings => Path.Combine(Data, "settings.json");
    public static string History => Path.Combine(Data, "history.json");
}

internal static class JsonFile
{
    internal static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, Options), new UTF8Encoding(false));
            File.Move(temporary, path, true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}

internal sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 2;
    public bool TextEnabled { get; set; } = true;
    public bool DoubleCopy { get; set; } = true;
    public int DoubleCopyInterval { get; set; } = 650;
    public ShortcutSpec TextShortcut { get; set; } = new(3, 0x20);
    public bool CaptureEnabled { get; set; } = true;
    public ShortcutSpec CaptureShortcut { get; set; } = new(3, 0x54);
    public bool HistoryEnabled { get; set; } = true;
    public int Direction { get; set; }
    public AppSettings Clone() => (AppSettings)MemberwiseClone();
    public static AppSettings Load(string path)
    {
        if (!File.Exists(path)) return new AppSettings();
        var value = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
        value.DoubleCopyInterval = Math.Clamp(value.DoubleCopyInterval, 250, 1500);
        value.Direction = Math.Clamp(value.Direction, 0, 2);
        value.TextShortcut ??= new(3, 0x20);
        value.CaptureShortcut ??= new(3, 0x54);
        return value;
    }
}

internal sealed class HistoryEntry
{
    public DateTimeOffset At { get; set; } = DateTimeOffset.Now;
    public string Source { get; set; } = "";
    public string Translation { get; set; } = "";
    public string SourceLanguage { get; set; } = "en";
    public string TargetLanguage { get; set; } = "ru";
    [JsonIgnore]
    public string Caption => $"{At.ToLocalTime():dd.MM HH:mm}   ·   {SourceLanguage.ToUpperInvariant()} → {TargetLanguage.ToUpperInvariant()}";
    [JsonIgnore] public string Preview
    {
        get { string text = Source.Replace("\r", "").Replace("\n", " ").Trim(); return text.Length > 130 ? text[..130] + "…" : text; }
    }
}

internal sealed class HistoryStore
{
    public const int Limit = 50;
    private readonly string path;
    private volatile List<HistoryEntry> entries = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    public IReadOnlyList<HistoryEntry> Entries => entries;
    public HistoryStore(string path, bool load = true)
    {
        this.path = path;
        if (!load || !File.Exists(path)) return;
        var loaded = JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(path));
        entries = (loaded ?? new()).Where(e => e != null && !string.IsNullOrWhiteSpace(e.Source)
            && !string.IsNullOrWhiteSpace(e.Translation) && e.Source.Length <= 20000 && e.Translation.Length <= 60000
            && e.SourceLanguage is "en" or "ru" && e.TargetLanguage is "en" or "ru" && e.SourceLanguage != e.TargetLanguage)
            .Take(Limit).ToList();
    }
    public void Add(string source, string translation, string from, string to)
        => AddAsync(source, translation, from, to).GetAwaiter().GetResult();
    public async Task AddAsync(string source, string translation, string from, string to)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var next = entries.Where(e => e.Source != source || e.SourceLanguage != from || e.TargetLanguage != to).ToList();
            next.Insert(0, new HistoryEntry { Source = source, Translation = translation, SourceLanguage = from, TargetLanguage = to });
            next = next.Take(Limit).ToList();
            await Task.Run(() => JsonFile.Write(path, next)).ConfigureAwait(false);
            entries = next;
        }
        finally { gate.Release(); }
    }
    public void Clear() => ClearAsync().GetAwaiter().GetResult();
    public async Task ClearAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try { await Task.Run(() => JsonFile.Write(path, Array.Empty<HistoryEntry>())).ConfigureAwait(false); entries = new(); }
        finally { gate.Release(); }
    }
    public IEnumerable<HistoryEntry> Search(string query) => entries.Where(e =>
        e.Source.Contains(query, StringComparison.OrdinalIgnoreCase) || e.Translation.Contains(query, StringComparison.OrdinalIgnoreCase));
}

internal static class Autostart
{
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "QuickTranslate";
    internal static string CommandFor(string launcher) => "\"" + Path.GetFullPath(launcher) + "\" --background";
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
        return string.Equals(key?.GetValue(ValueName) as string, CommandFor(PortablePaths.Launcher), StringComparison.OrdinalIgnoreCase);
    }
    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, true)
            ?? throw new IOException("Не удалось открыть настройки автозапуска Windows.");
        if (enabled) key.SetValue(ValueName, CommandFor(PortablePaths.Launcher), RegistryValueKind.String);
        else key.DeleteValue(ValueName, false);
    }
}
