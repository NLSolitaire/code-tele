using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace UberDownloader.Core.Services;

public class AutoUpdateService
{
    public static async Task CheckAndUpdateYtDlpSilentlyAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                string ytdlpExe = ToolLocator.GetYtDlpPath();
                var psi = new ProcessStartInfo
                {
                    FileName = ytdlpExe,
                    Arguments = "-U",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                proc?.WaitForExit(15000);
            }
            catch
            {
                // Silently ignore network failures on startup
            }
        });
    }

    public static async Task<bool> ForceUpdateYtDlpAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                string ytdlpExe = ToolLocator.GetYtDlpPath();
                var psi = new ProcessStartInfo
                {
                    FileName = ytdlpExe,
                    Arguments = "-U",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    proc.WaitForExit(30000);
                    return proc.ExitCode == 0;
                }
            }
            catch { }
            return false;
        });
    }
}
