using System;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Extensions.DependencyInjection;
using UberDownloader.Core.Data;
using UberDownloader.Core.Services;
using UberDownloader.Wpf.ViewModels;
using UberDownloader.Wpf.Views;

namespace UberDownloader.Wpf;

public partial class App : Application
{
    private readonly IServiceProvider _serviceProvider;
    private SingleInstanceManager? _singleInstanceManager;
    private FloatingBarWindow? _floatingBarWindow;
    private MainWindow? _mainWindow;
    private const int HotkeyId = 9001;
    private HwndSource? _hwndSource;

    public App()
    {
        SetupExceptionHandling();
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
    }

    private void SetupExceptionHandling()
    {
        DispatcherUnhandledException += (s, e) =>
        {
            try
            {
                Logger.Log($"[FATAL CRASH / DISPATCHER] {e.Exception.Message}\n{e.Exception.StackTrace}");
                string title = LocalizationService.Instance["CrashDialogTitle"];
                string msg = $"{LocalizationService.Instance["CrashDialogMessage"]}\n\n{e.Exception.Message}";
                MessageBox.Show(msg, title, MessageBoxButton.OK, MessageBoxImage.Error);
                e.Handled = true;
            }
            catch
            {
                // Prevent crash loop
            }
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            try
            {
                if (e.ExceptionObject is Exception ex)
                {
                    Logger.Log($"[FATAL CRASH / APPDOMAIN] {ex.Message}\n{ex.StackTrace}");
                }
            }
            catch
            {
            }
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            try
            {
                Logger.Log($"[FATAL / UNOBSERVED TASK] {e.Exception.Message}\n{e.Exception.StackTrace}");
                e.SetObserved();
            }
            catch
            {
            }
        };
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<DatabaseService>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<MediaIntegrityService>();
        services.AddSingleton<DuplicateDetectionService>();
        services.AddSingleton<MediaInspector>();
        services.AddSingleton<PlaylistService>();
        services.AddSingleton<DownloadService>();
        services.AddSingleton<DownloadScheduler>();
        services.AddSingleton<SingleInstanceManager>();

        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<PlaylistPreparationViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Logger.Log(UberDownloader.Core.Services.LocalizationService.Instance["LogAppStarting"]);

        _singleInstanceManager = _serviceProvider.GetRequiredService<SingleInstanceManager>();

        string initialUrl = e.Args.Length > 0 ? e.Args[0] : string.Empty;

        // 1. Contrôle d'instance unique
        if (!_singleInstanceManager.CheckAndAcquire())
        {
            string msg = !string.IsNullOrWhiteSpace(initialUrl) ? initialUrl : "SHOW";
            await SingleInstanceManager.SendMessageToRunningInstanceAsync(msg);
            Shutdown();
            return;
        }

        var settingsService = _serviceProvider.GetRequiredService<SettingsService>();
        UberDownloader.Wpf.Services.ThemeService.Initialize(settingsService.Settings.Theme);

        _mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        var viewModel = _serviceProvider.GetRequiredService<MainViewModel>();

        // 2. Gestion des messages IPC reçus d'une seconde instance
        _singleInstanceManager.OnUrlOrMessageReceived += (msg) =>
        {
            Dispatcher.Invoke(() =>
            {
                if (msg == "SHOW")
                {
                    RestoreMainWindow();
                }
                else if (msg.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    RestoreMainWindow();
                    viewModel.InputUrl = msg;
                }
            });
        };

        // 3. Barre flottante Always-On-Top
        _floatingBarWindow = new FloatingBarWindow(
            onAddUrl: (url) =>
            {
                Dispatcher.Invoke(() =>
                {
                    viewModel.InputUrl = url;
                    viewModel.StartDownloadCommand.Execute(null);
                });
            },
            onRestoreFullWindow: () =>
            {
                RestoreMainWindow();
            },
            onOpenSettings: (url) =>
            {
                Dispatcher.Invoke(() =>
                {
                    RestoreMainWindow();
                    viewModel.IsSettingsOpen = false;
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        viewModel.InputUrl = url;
                    }
                    viewModel.IsAdvancedOpen = true;
                });
            },
            mainViewModel: viewModel);

        _mainWindow.Loaded += (s, ev) =>
        {
            SetupGlobalHotkey();
        };

        viewModel.RequestHideWindow = () =>
        {
            _mainWindow?.Hide();
        };

        viewModel.RequestExplicitShutdown = () =>
        {
            ExplicitShutdown();
        };

        // 4. Fermeture de la fenêtre : si des téléchargements sont en cours, proposer le choix (fond, pause, annulation)
        _mainWindow.Closing += (s, ev) =>
        {
            if (!_isExplicitExit)
            {
                ev.Cancel = true;
                if (viewModel.HasActiveDownloads)
                {
                    viewModel.IsExitConfirmOpen = true;
                }
                else
                {
                    _mainWindow.Hide();
                }
            }
        };

        _mainWindow.Show();

        if (!string.IsNullOrWhiteSpace(initialUrl))
        {
            viewModel.InputUrl = initialUrl;
        }

        // 5. Vérification silencieuse de mise à jour yt-dlp
        _ = AutoUpdateService.CheckAndUpdateYtDlpSilentlyAsync();
    }

    private bool _isExplicitExit = false;
    public bool IsExplicitExit => _isExplicitExit;

    public async void ExplicitShutdown()
    {
        _isExplicitExit = true;
        _floatingBarWindow?.Close();

        var downloadScheduler = _serviceProvider.GetService<DownloadScheduler>();
        downloadScheduler?.PauseAll();

        var downloadService = _serviceProvider.GetService<DownloadService>();
        if (downloadService != null && downloadService.ActiveLiveItems.Count > 0)
        {
            if (_mainWindow != null)
            {
                _mainWindow.Title = LocalizationService.Instance["LiveFinalizingTitle"];
            }
            await downloadService.StopAndFinalizeAllLivesAsync();
        }

        _mainWindow?.Close();
        Shutdown();
    }

    public void RestoreMainWindow()
    {
        if (_floatingBarWindow != null && _floatingBarWindow.IsVisible)
        {
            _floatingBarWindow.Hide();
        }

        if (_mainWindow != null)
        {
            if (!_mainWindow.IsVisible) _mainWindow.Show();
            if (_mainWindow.WindowState == WindowState.Minimized) _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
            _mainWindow.Focus();

            var helper = new WindowInteropHelper(_mainWindow);
            if (helper.Handle != IntPtr.Zero)
            {
                NativeMethods.SetForegroundWindow(helper.Handle);
            }
        }
    }

    public void ShowFloatingBar()
    {
        _mainWindow?.Hide();
        _floatingBarWindow?.Show();
    }

    public void ToggleMainWindow()
    {
        if (_mainWindow == null) return;

        bool isWindowOpenAndActive = _mainWindow.IsVisible && 
                                     _mainWindow.WindowState != WindowState.Minimized && 
                                     _mainWindow.IsActive;

        if (isWindowOpenAndActive)
        {
            _mainWindow.Hide();
        }
        else
        {
            RestoreMainWindow();
        }
    }

    public void ApplyGlobalHotkey(string? hotkeyText)
    {
        if (_mainWindow == null) return;
        var helper = new WindowInteropHelper(_mainWindow);
        if (helper.Handle == IntPtr.Zero) return;

        NativeMethods.UnregisterHotKey(helper.Handle, HotkeyId);

        string effectiveHotkey = !string.IsNullOrWhiteSpace(hotkeyText) ? hotkeyText : "Ctrl+Shift+O";
        if (HotkeyHelper.TryParse(effectiveHotkey, out uint fsModifiers, out uint vk))
        {
            NativeMethods.RegisterHotKey(helper.Handle, HotkeyId, fsModifiers, vk);
        }
    }

    private void SetupGlobalHotkey()
    {
        if (_mainWindow == null) return;
        var helper = new WindowInteropHelper(_mainWindow);
        if (helper.Handle == IntPtr.Zero) return;

        _hwndSource = HwndSource.FromHwnd(helper.Handle);
        _hwndSource?.AddHook(HwndHook);

        var settings = _serviceProvider.GetRequiredService<SettingsService>().Settings;
        ApplyGlobalHotkey(settings.GlobalHotkey);
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            ToggleMainWindow();
            handled = true;
        }
        return IntPtr.Zero;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_mainWindow != null)
        {
            var helper = new WindowInteropHelper(_mainWindow);
            if (helper.Handle != IntPtr.Zero)
            {
                NativeMethods.UnregisterHotKey(helper.Handle, HotkeyId);
            }
        }

        _hwndSource?.Dispose();
        _singleInstanceManager?.Dispose();
        base.OnExit(e);
    }
}
