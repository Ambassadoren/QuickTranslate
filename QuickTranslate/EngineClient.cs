using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace QuickTranslate;

internal sealed class EngineClient : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private Process? process;
    private int sequence;
    private readonly string root = AppContext.BaseDirectory;

    private async Task EnsureStartedAsync()
    {
        if (process != null && !process.HasExited) return;
        DisposeProcess();
        var start = new ProcessStartInfo(Path.Combine(root, "runtime", "python.exe"))
        {
            WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        start.ArgumentList.Add("-u");
        start.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
        start.ArgumentList.Add(Path.Combine(root, "backend.py"));
        process = Process.Start(start) ?? throw new IOException("Не удалось запустить движок перевода.");
        // Drain stderr to avoid a blocked pipe; user texts are never logged.
        _ = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ready = await process.StandardOutput.ReadLineAsync(timeout.Token);
        if (ready == null || !ready.Contains("\"ready\": true"))
            throw new IOException("Движок не загрузился. Проверьте, что папки runtime и models находятся рядом с приложением.");
    }

    public async Task<string> TranslateAsync(string text, string source, string target, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureStartedAsync();
            var request = JsonSerializer.Serialize(new { id = ++sequence, text, source, target });
            await process!.StandardInput.WriteLineAsync(request);
            await process.StandardInput.FlushAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            var line = await process.StandardOutput.ReadLineAsync(timeout.Token);
            if (line == null) throw new IOException("Движок перевода остановился. Повторите попытку.");
            using var response = JsonDocument.Parse(line);
            if (response.RootElement.TryGetProperty("error", out var error))
                throw new IOException("Ошибка локальной модели: " + error.GetString());
            return response.RootElement.GetProperty("text").GetString() ?? "";
        }
        catch (OperationCanceledException)
        {
            DisposeProcess();
            throw new IOException("Перевод занял слишком много времени. Попробуйте более короткий фрагмент.");
        }
        catch
        {
            DisposeProcess();
            throw;
        }
        finally { gate.Release(); }
    }

    private void DisposeProcess()
    {
        try { if (process != null && !process.HasExited) process.Kill(true); } catch { }
        process?.Dispose();
        process = null;
    }
    public void Dispose() => DisposeProcess();
}
