using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using UberDownloader.Core.Services;
using UberDownloader.Wpf.ViewModels;

namespace UberDownloader.Wpf.Views;

public partial class FloatingBarWindow : Window, INotifyPropertyChanged
{
    private readonly Action<string> _onAddUrl;
    private readonly Action _onRestoreFullWindow;
    private readonly Action<string> _onOpenSettings;
    private readonly DispatcherTimer _inactivityTimer;

    public MainViewModel MainVm { get; }
    public LocalizationService Loc => LocalizationService.Instance;

    public int ActiveDownloadsCount => MainVm.ActiveDownloadsCount;

    public event PropertyChangedEventHandler? PropertyChanged;

    public FloatingBarWindow(
        Action<string> onAddUrl,
        Action onRestoreFullWindow,
        Action<string> onOpenSettings,
        MainViewModel mainViewModel)
    {
        InitializeComponent();
        MainVm = mainViewModel;
        DataContext = this;

        _onAddUrl = onAddUrl;
        _onRestoreFullWindow = onRestoreFullWindow;
        _onOpenSettings = onOpenSettings;

        MainVm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.ActiveDownloadsCount))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActiveDownloadsCount)));
            }
        };

        DragHandle.MouseDown += (s, e) =>
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        };

        // Touche Entrée pour lancer directement le téléchargement et vider le champ
        BarUrlBox.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter)
            {
                OnDownloadClicked(this, new RoutedEventArgs());
                e.Handled = true;
            }
        };

        // Minuterie d'inactivité de 10 secondes (baisse d'opacité à 50%)
        _inactivityTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(10)
        };
        _inactivityTimer.Tick += OnInactivityTick;

        Loaded += (s, e) =>
        {
            Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
            Top = 40;
            EnsureTopmostPermanent();
            ResetOpacityAndTimer();
        };

        Deactivated += (s, e) =>
        {
            EnsureTopmostPermanent();
        };

        IsVisibleChanged += (s, e) =>
        {
            if (IsVisible)
            {
                ResetOpacityAndTimer();
            }
            else
            {
                _inactivityTimer.Stop();
            }
        };

        // Événements d'interaction utilisateur pour rétablir 100% d'opacité
        MouseEnter += (s, e) => ResetOpacityAndTimer();
        PreviewMouseMove += (s, e) =>
        {
            if (Opacity < 1.0)
            {
                ResetOpacityAndTimer();
            }
            else
            {
                _inactivityTimer.Stop();
                _inactivityTimer.Start();
            }
        };
        PreviewKeyDown += (s, e) => ResetOpacityAndTimer();
        GotFocus += (s, e) => ResetOpacityAndTimer();
        MouseLeave += (s, e) =>
        {
            _inactivityTimer.Stop();
            _inactivityTimer.Start();
        };
    }

    public void ClearUrl()
    {
        BarUrlBox.Text = string.Empty;
    }

    private void OnInactivityTick(object? sender, EventArgs e)
    {
        if (IsMouseOver || BarUrlBox.IsFocused)
        {
            _inactivityTimer.Stop();
            _inactivityTimer.Start();
            return;
        }

        var fadeOut = new DoubleAnimation
        {
            To = 0.5,
            Duration = TimeSpan.FromMilliseconds(300),
            FillBehavior = FillBehavior.HoldEnd
        };
        BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private void ResetOpacityAndTimer()
    {
        _inactivityTimer.Stop();

        if (Opacity < 1.0)
        {
            var fadeIn = new DoubleAnimation
            {
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(150),
                FillBehavior = FillBehavior.HoldEnd
            };
            BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }
        else
        {
            BeginAnimation(UIElement.OpacityProperty, null);
            Opacity = 1.0;
        }

        _inactivityTimer.Start();
    }

    private void EnsureTopmostPermanent()
    {
        var helper = new WindowInteropHelper(this);
        if (helper.Handle != IntPtr.Zero)
        {
            NativeMethods.SetWindowPos(helper.Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);
        }
    }

    private void OnSettingsClicked(object sender, RoutedEventArgs e)
    {
        ResetOpacityAndTimer();
        string url = BarUrlBox.Text.Trim();
        _onOpenSettings(url);
    }

    private void OnDownloadClicked(object sender, RoutedEventArgs e)
    {
        ResetOpacityAndTimer();
        string url = BarUrlBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(url))
        {
            BarUrlBox.Text = string.Empty;
            _onAddUrl(url);
        }
    }

    private void OnRestoreClicked(object sender, RoutedEventArgs e)
    {
        ResetOpacityAndTimer();
        _onRestoreFullWindow();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        _inactivityTimer.Stop();
        Hide();
    }
}
