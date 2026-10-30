using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace UberDownloader.Core.Services;

public record SniffResult(bool Success, string StreamUrl, string Referer, string Title, string Error);

public class StreamSnifferService
{
    private static readonly string[] AdPatterns = new[]
    {
        "google-analytics", "doubleclick", "statcounter", "facebook.com", "adservice",
        "adsystem", "syndication", "adnxs", "exoclick", "adsterra", "popads", "popcash",
        "trafficjunky", "juicyads", "propellerads", "monetag", "hilltopads", "clickadu",
        "outbrain", "taboola", "adcolony", "vungle", "unityads", "applovin", "adzerk",
        "smartadserver", "criteo", "zergnet", "adform", "yandex", "bidvertiser", "mgid",
        "a-ads", "coinhive", "spotxchange", "pubmatic", "openx", "rubiconproject",
        "casalemedia", "ad-delivery", "advertising", "/ads/", "/ad/", "advert",
        "preroll", "midroll", "postroll", "vast", "vpaid", "popunder", "banner",
        "track", "telemetry", "beacon", "analytics", "pixel"
    };

    private static bool IsAd(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return true;
        string lower = url.ToLowerInvariant();
        return AdPatterns.Any(p => lower.Contains(p));
    }

    private static void EnsureDriverConfigured()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string playwrightDir = Path.Combine(baseDir, ".playwright");

        if (Directory.Exists(playwrightDir))
        {
            Environment.SetEnvironmentVariable("PLAYWRIGHT_DRIVER_SEARCH_PATH", baseDir);
        }
        else
        {
            // Recherche dans les chemins de solution si exécuté en dev
            string devPath = Path.Combine(baseDir, "..", "..", "..", "..", "UberDownloader.Core", "bin", "Release", "net10.0");
            if (Directory.Exists(Path.Combine(devPath, ".playwright")))
            {
                Environment.SetEnvironmentVariable("PLAYWRIGHT_DRIVER_SEARCH_PATH", Path.GetFullPath(devPath));
            }
        }
    }

    private static string? FindBrowserExecutable(string targetName)
    {
        string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string userProf = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var known = new Dictionary<string, string[]>
        {
            ["opera gx"] = new[]
            {
                Path.Combine(localApp, "Programs", "Opera GX", "opera.exe"),
                Path.Combine(localApp, "Programs", "Opera GX", "launcher.exe"),
                Path.Combine(progFiles, "Opera GX", "launcher.exe")
            },
            ["opera developer"] = new[]
            {
                Path.Combine(localApp, "Programs", "Opera developer", "opera.exe"),
                Path.Combine(localApp, "Programs", "Opera developer", "launcher.exe"),
                Path.Combine(progFiles, "Opera developer", "launcher.exe")
            },
            ["opera beta"] = new[]
            {
                Path.Combine(localApp, "Programs", "Opera beta", "opera.exe"),
                Path.Combine(localApp, "Programs", "Opera beta", "launcher.exe"),
                Path.Combine(progFiles, "Opera beta", "launcher.exe")
            },
            ["opera"] = new[]
            {
                Path.Combine(localApp, "Programs", "Opera", "opera.exe"),
                Path.Combine(localApp, "Programs", "Opera", "launcher.exe"),
                Path.Combine(progFiles, "Opera", "launcher.exe"),
                Path.Combine(progFilesX86, "Opera", "launcher.exe")
            },
            ["brave"] = new[]
            {
                Path.Combine(progFiles, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
                Path.Combine(progFilesX86, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
                Path.Combine(localApp, "BraveSoftware", "Brave-Browser", "Application", "brave.exe")
            },
            ["tor"] = new[]
            {
                Path.Combine(userProf, "Desktop", "Tor Browser", "Browser", "firefox.exe"),
                Path.Combine(userProf, "Downloads", "Tor Browser", "Browser", "firefox.exe"),
                Path.Combine(localApp, "Tor Browser", "Browser", "firefox.exe"),
                @"C:\Tor Browser\Browser\firefox.exe",
                Path.Combine(progFiles, "Tor Browser", "Browser", "firefox.exe"),
                Path.Combine(progFilesX86, "Tor Browser", "Browser", "firefox.exe")
            },
            ["chrome"] = new[]
            {
                Path.Combine(progFiles, "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(progFilesX86, "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(localApp, "Google", "Chrome", "Application", "chrome.exe")
            },
            ["msedge"] = new[]
            {
                Path.Combine(progFiles, "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(progFilesX86, "Microsoft", "Edge", "Application", "msedge.exe")
            },
            ["firefox"] = new[]
            {
                Path.Combine(progFiles, "Mozilla Firefox", "firefox.exe"),
                Path.Combine(progFilesX86, "Mozilla Firefox", "firefox.exe")
            }
        };

        string key = targetName.ToLowerInvariant().Trim();
        foreach (var (k, paths) in known)
        {
            if (k.Contains(key) || key.Contains(k))
            {
                foreach (var p in paths)
                {
                    if (File.Exists(p)) return p;
                }
            }
        }

        return null;
    }

    public async Task<SniffResult> SniffAsync(string url, bool headless, string browser = "Chromium", int timeoutSeconds = 60, CancellationToken cancellationToken = default)
    {
        try
        {
            EnsureDriverConfigured();
        }
        catch (Exception ex)
        {
            Logger.Log($"[SNIFFER] Note configuration driver: {ex.Message}");
        }

        IPlaywright? playwright = null;
        IBrowser? browserInstance = null;
        IBrowserContext? context = null;

        var capturedList = new List<string>();
        var lockObj = new object();
        string finalReferer = url;
        string pageTitle = string.Empty;

        void HandleUrlCandidate(string? cand)
        {
            if (string.IsNullOrWhiteSpace(cand) || IsAd(cand)) return;
            if (Regex.IsMatch(cand, @"(\.m3u8|\.mpd|\.mp4|\/hls\/|\/manifest)(\?|$)", RegexOptions.IgnoreCase) || cand.Contains("master", StringComparison.OrdinalIgnoreCase))
            {
                lock (lockObj)
                {
                    if (!capturedList.Contains(cand))
                    {
                        capturedList.Add(cand);
                    }
                }
            }
        }

        try
        {
            playwright = await Playwright.CreateAsync();

            string choice = (browser ?? "Chromium").ToLowerInvariant().Trim();
            var launchTasks = new List<Func<Task<IBrowser>>>();

            string[] blinkArgs = new[] { "--disable-blink-features=AutomationControlled" };

            // 1. Tor Browser
            if (choice.Contains("tor"))
            {
                string? torExe = FindBrowserExecutable("tor");
                if (!string.IsNullOrEmpty(torExe))
                {
                    launchTasks.Add(() => playwright.Firefox.LaunchAsync(new BrowserTypeLaunchOptions { ExecutablePath = torExe, Headless = headless }));
                }
            }
            // 2. Brave
            else if (choice.Contains("brave"))
            {
                string? braveExe = FindBrowserExecutable("brave");
                if (!string.IsNullOrEmpty(braveExe))
                {
                    launchTasks.Add(() => playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { ExecutablePath = braveExe, Headless = headless, Args = blinkArgs }));
                }
            }
            // 3. Opera GX
            else if (choice.Contains("opera gx"))
            {
                string? ogxExe = FindBrowserExecutable("opera gx");
                if (!string.IsNullOrEmpty(ogxExe))
                {
                    launchTasks.Add(() => playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { ExecutablePath = ogxExe, Headless = headless, Args = blinkArgs }));
                }
            }
            // 4. Opera Developer / Beta / Standard
            else if (choice.Contains("opera"))
            {
                string? operaExe = FindBrowserExecutable(choice) ?? FindBrowserExecutable("opera");
                if (!string.IsNullOrEmpty(operaExe))
                {
                    launchTasks.Add(() => playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { ExecutablePath = operaExe, Headless = headless, Args = blinkArgs }));
                }
            }
            // 5. Firefox
            else if (choice.Contains("firefox"))
            {
                string? ffExe = FindBrowserExecutable("firefox");
                if (!string.IsNullOrEmpty(ffExe))
                {
                    launchTasks.Add(() => playwright.Firefox.LaunchAsync(new BrowserTypeLaunchOptions { ExecutablePath = ffExe, Headless = headless }));
                }
                else
                {
                    launchTasks.Add(() => playwright.Firefox.LaunchAsync(new BrowserTypeLaunchOptions { Headless = headless }));
                }
            }
            // 6. Google Chrome
            else if (choice.Contains("chrome") && !choice.Contains("chromium"))
            {
                launchTasks.Add(() => playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Channel = "chrome", Headless = headless, Args = blinkArgs }));
            }
            // 7. Microsoft Edge
            else if (choice.Contains("edge") || choice.Contains("msedge"))
            {
                launchTasks.Add(() => playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Channel = "msedge", Headless = headless, Args = blinkArgs }));
            }
            // 8. Chromium par défaut
            else
            {
                launchTasks.Add(() => playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Channel = "msedge", Headless = headless, Args = blinkArgs }));
                launchTasks.Add(() => playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = headless, Args = blinkArgs }));
            }

            // Fallbacks systématiques toujours garantis sous Windows (Edge est préinstallé sur 100% des Windows 10/11)
            launchTasks.Add(() => playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Channel = "msedge", Headless = headless, Args = blinkArgs }));
            launchTasks.Add(() => playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Channel = "chrome", Headless = headless, Args = blinkArgs }));
            launchTasks.Add(() => playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = headless, Args = blinkArgs }));

            Exception? lastEx = null;
            foreach (var launcher in launchTasks)
            {
                try
                {
                    browserInstance = await launcher();
                    if (browserInstance != null) break;
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                }
            }

            if (browserInstance == null)
            {
                return new SniffResult(false, string.Empty, string.Empty, string.Empty, $"Impossible de démarrer le navigateur ({browser}) : {lastEx?.Message}");
            }

            context = await browserInstance.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
                ViewportSize = new ViewportSize { Width = 1280, Height = 720 }
            });

            var page = await context.NewPageAsync();

            void AttachListeners(IPage p)
            {
                p.Request += (_, req) =>
                {
                    try { HandleUrlCandidate(req.Url); } catch { }
                };
                p.Response += (_, resp) =>
                {
                    try
                    {
                        if (IsAd(resp.Url)) return;
                        var headers = resp.Headers;
                        if (headers != null && headers.TryGetValue("content-type", out var ct) && !string.IsNullOrWhiteSpace(ct))
                        {
                            string ctl = ct.ToLowerInvariant();
                            if (ctl.Contains("mpegurl") || ctl.Contains("dash+xml") || ctl.Contains("video/mp4"))
                            {
                                HandleUrlCandidate(resp.Url);
                            }
                        }
                    }
                    catch { }
                };
            }

            AttachListeners(page);
            context.Page += (_, p) => AttachListeners(p);

            try
            {
                await page.GotoAsync(url, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = timeoutSeconds * 1000
                });
                finalReferer = page.Url;
                try
                {
                    pageTitle = await page.TitleAsync();
                }
                catch { }

                // En mode headless, cliquer automatiquement sur les boutons de lecture
                if (headless)
                {
                    await Task.Delay(2000, cancellationToken);
                    var selectors = new[]
                    {
                        ".play", "#play", "video", ".vjs-big-play-button",
                        ".jw-display-icon-display", ".jw-icon-playback", ".jw-display-icon-container",
                        "button"
                    };

                    for (int attempt = 0; attempt < 3 && !cancellationToken.IsCancellationRequested; attempt++)
                    {
                        foreach (var frame in page.Frames)
                        {
                            foreach (var sel in selectors)
                            {
                                try
                                {
                                    var loc = frame.Locator(sel).First;
                                    if (await loc.IsVisibleAsync())
                                    {
                                        await loc.ClickAsync(new LocatorClickOptions { Timeout = 500 });
                                        break;
                                    }
                                }
                                catch { }
                            }
                        }

                        lock (lockObj)
                        {
                            if (capturedList.Count > 0) break;
                        }

                        try
                        {
                            await page.Mouse.ClickAsync(640, 360);
                        }
                        catch { }

                        await Task.Delay(1500, cancellationToken);
                    }
                }

                // Boucle d'attente intelligente
                var sw = Stopwatch.StartNew();
                int maxWaitMs = timeoutSeconds * 1000;

                while (sw.ElapsedMilliseconds < maxWaitMs && !cancellationToken.IsCancellationRequested)
                {
                    lock (lockObj)
                    {
                        if (capturedList.Count > 0)
                        {
                            bool hasHls = capturedList.Any(u => u.Contains(".m3u8", StringComparison.OrdinalIgnoreCase) ||
                                                                u.Contains(".mpd", StringComparison.OrdinalIgnoreCase) ||
                                                                u.Contains("/hls/", StringComparison.OrdinalIgnoreCase));
                            if (hasHls) break;
                            if (headless) break;
                            if (sw.ElapsedMilliseconds > 15000) break;
                        }
                    }

                    // En mode interactif visible : si l'utilisateur a fermé toutes les fenêtres du navigateur
                    if (!headless)
                    {
                        var activePages = context.Pages.Where(p => !p.IsClosed).ToList();
                        if (activePages.Count == 0)
                        {
                            break;
                        }
                    }

                    await Task.Delay(500, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[SNIFFER] Navigation note: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            return new SniffResult(false, string.Empty, string.Empty, string.Empty, ex.Message);
        }
        finally
        {
            try { if (context != null) await context.CloseAsync(); } catch { }
            try { if (browserInstance != null) await browserInstance.CloseAsync(); } catch { }
            try { playwright?.Dispose(); } catch { }
        }

        lock (lockObj)
        {
            if (capturedList.Count > 0)
            {
                var masters = capturedList.Where(u => u.Contains("master", StringComparison.OrdinalIgnoreCase)).ToList();
                var m3u8s = capturedList.Where(u => u.Contains(".m3u8", StringComparison.OrdinalIgnoreCase) ||
                                                    u.Contains(".mpd", StringComparison.OrdinalIgnoreCase) ||
                                                    u.Contains("/hls/", StringComparison.OrdinalIgnoreCase)).ToList();

                string bestUrl = masters.Count > 0 ? masters[0] : (m3u8s.Count > 0 ? m3u8s[0] : capturedList[0]);
                return new SniffResult(true, bestUrl, finalReferer, pageTitle, string.Empty);
            }
            else
            {
                return new SniffResult(false, string.Empty, string.Empty, string.Empty, LocalizationService.Instance["SniffStreamFailed"]);
            }
        }
    }
}
