using System;
using System.Windows;
using System.Windows.Threading;
using UberDownloader.Wpf.ViewModels;

namespace UberDownloader.Wpf;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _clipboardTimer;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        Loaded += async (s, e) =>
        {
            await _viewModel.InitializeAsync();
            _viewModel.CheckClipboardForUrl();
        };

        Activated += (s, e) =>
        {
            _viewModel.CheckClipboardForUrl();
        };

        _clipboardTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1.5)
        };
        _clipboardTimer.Tick += (s, e) =>
        {
            _viewModel.CheckClipboardForUrl();
        };
        _clipboardTimer.Start();
    }

    private void OnFloatingBarClicked(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App app)
        {
            app.ShowFloatingBar();
        }
    }

    private void OnCardsScrollViewerPreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (sender is System.Windows.Controls.ScrollViewer scv)
        {
            // Réduit la vitesse de défilement pour un contrôle précis et fluide
            double targetOffset = scv.VerticalOffset - (e.Delta * 0.35);
            if (targetOffset < 0) targetOffset = 0;
            if (targetOffset > scv.ScrollableHeight) targetOffset = scv.ScrollableHeight;
            scv.ScrollToVerticalOffset(targetOffset);
            e.Handled = true;
        }
    }
}
