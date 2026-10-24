using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace UberDownloader.Core.Services;

public record SniffResult(bool Success, string StreamUrl, string Referer, string Title, string Error);

public class StreamSnifferService
{
    public async Task<SniffResult> SniffAsync(string url, bool headless, string browser = "Chromium", int timeoutSeconds = 60, CancellationToken cancellationToken = default)
    {
        string pythonExe = ToolLocator.GetPythonPath();
        string scriptPath = ToolLocator.GetSnifferScriptPath();

        if (!File.Exists(scriptPath))
        {
            return new SniffResult(false, string.Empty, string.Empty, string.Empty, $"Script introuvable: {scriptPath}");
        }

        string headlessFlag = headless ? "--headless" : string.Empty;
        string browserFlag = !string.IsNullOrWhiteSpace(browser) ? $"--browser \"{browser}\"" : string.Empty;
        var psi = new ProcessStartInfo
        {
            FileName = pythonExe,
            Arguments = $"\"{scriptPath}\" --url \"{url}\" {headlessFlag} {browserFlag} --timeout {timeoutSeconds}".Trim(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true // La fenêtre du navigateur s'affiche indépendamment du processus console
        };

        try
        {
            using var proc = new Process { StartInfo = psi };
            proc.Start();

            var stdoutTask = proc.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = proc.StandardError.ReadToEndAsync(cancellationToken);

            var exitTask = proc.WaitForExitAsync(cancellationToken);
            if (await Task.WhenAny(exitTask, Task.Delay((timeoutSeconds + 30) * 1000, cancellationToken)) == exitTask)
            {
                string stdout = await stdoutTask;
                string stderr = await stderrTask;

                if (!string.IsNullOrWhiteSpace(stdout))
                {
                    var lines = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        if (line.TrimStart().StartsWith("{") && line.TrimEnd().EndsWith("}"))
                        {
                            try
                            {
                                using var doc = JsonDocument.Parse(line);
                                var root = doc.RootElement;
                                bool success = root.TryGetProperty("success", out var sProp) && sProp.GetBoolean();
                                string streamUrl = root.TryGetProperty("stream_url", out var uProp) ? (uProp.GetString() ?? "") : "";
                                string referer = root.TryGetProperty("referer", out var rProp) ? (rProp.GetString() ?? "") : "";
                                string title = root.TryGetProperty("title", out var tProp) ? (tProp.GetString() ?? "") : "";
                                string error = root.TryGetProperty("error", out var eProp) ? (eProp.GetString() ?? "") : "";

                                return new SniffResult(success, streamUrl, referer, title, error);
                            }
                            catch { }
                        }
                    }
                }

                return new SniffResult(false, string.Empty, string.Empty, string.Empty, !string.IsNullOrWhiteSpace(stderr) ? stderr : "Aucune réponse de l'analyseur.");
            }
            else
            {
                try { proc.Kill(true); } catch { }
                return new SniffResult(false, string.Empty, string.Empty, string.Empty, LocalizationService.Instance["SniffTimeout"]);
            }
        }
        catch (Exception ex)
        {
            return new SniffResult(false, string.Empty, string.Empty, string.Empty, ex.Message);
        }
    }
}
