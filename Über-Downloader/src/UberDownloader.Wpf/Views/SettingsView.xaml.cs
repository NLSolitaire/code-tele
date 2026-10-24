using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using UberDownloader.Core.Services;
using UberDownloader.Wpf.ViewModels;

namespace UberDownloader.Wpf.Views;

public partial class SettingsView : UserControl
{
    private SettingsViewModel? _subscribedVm;
    private string _lastRawLogs = string.Empty;

    public SettingsView()
    {
        InitializeComponent();
        Focusable = true;
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        PreviewKeyDown += OnPreviewKeyDown;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged()
    {
        Dispatcher.Invoke(() =>
        {
            if (!string.IsNullOrEmpty(_lastRawLogs))
            {
                UpdateLogDocument(_lastRawLogs);
            }
            else
            {
                UpdateLogDocument(Logger.ReadAllLogs());
            }
        });
    }

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (DataContext is SettingsViewModel vm && vm.IsCapturingHotkey)
        {
            var key = (e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key);
            string? keyName = NormalizeKeyName(key);
            if (!string.IsNullOrEmpty(keyName))
            {
                vm.RegisterKeyStroke(keyName);
                e.Handled = true;
            }
        }
    }

    private static string? NormalizeKeyName(System.Windows.Input.Key key)
    {
        return key switch
        {
            System.Windows.Input.Key.LeftCtrl or System.Windows.Input.Key.RightCtrl => "Ctrl",
            System.Windows.Input.Key.LeftAlt or System.Windows.Input.Key.RightAlt => "Alt",
            System.Windows.Input.Key.LeftShift or System.Windows.Input.Key.RightShift => "Shift",
            System.Windows.Input.Key.LWin or System.Windows.Input.Key.RWin => "Win",
            System.Windows.Input.Key.Escape => null,
            System.Windows.Input.Key.Tab => null,
            _ => key.ToString()
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateLogDocument(Logger.ReadAllLogs());
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_subscribedVm != null)
        {
            _subscribedVm.LogsRefreshed -= OnLogsRefreshed;
        }

        if (DataContext is SettingsViewModel vm)
        {
            _subscribedVm = vm;
            _subscribedVm.LogsRefreshed += OnLogsRefreshed;
        }
    }

    private void OnLogsRefreshed(string rawLogs)
    {
        Dispatcher.Invoke(() => UpdateLogDocument(rawLogs));
    }

    public void UpdateLogDocument(string rawLogs)
    {
        _lastRawLogs = rawLogs;
        var doc = new FlowDocument
        {
            PagePadding = new Thickness(4),
            Background = Brushes.Transparent
        };

        var lines = rawLogs.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var nonEmpties = lines.Select((line, index) => (line, index))
                              .Where(x => !string.IsNullOrWhiteSpace(x.line))
                              .ToList();

        // SEULEMENT la dernière entrée (la plus récente) est en jaune, les précédentes sont blanches
        int lastIndex = nonEmpties.Count > 0 ? nonEmpties[nonEmpties.Count - 1].index : -1;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            var p = new Paragraph { Margin = new Thickness(0, 1, 0, 1) };
            bool isLast = (i == lastIndex);

            FormatLogLine(p, line, isLast);
            doc.Blocks.Add(p);
        }

        LogRichTextBox.Document = doc;
        ScrollLogsToBottom();
    }

    public void ScrollLogsToBottom()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                LogRichTextBox.ScrollToEnd();
            }
            catch { }
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void OnTabControlSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source is TabControl tc && tc.SelectedItem is TabItem ti)
        {
            // Dès qu'on ouvre l'onglet logs, faire défiler immédiatement tout en bas pour afficher le plus récent
            string headerText = ti.Header?.ToString() ?? string.Empty;
            if (headerText.Contains("Journal", StringComparison.OrdinalIgnoreCase) ||
                headerText.Contains("Logs", StringComparison.OrdinalIgnoreCase) ||
                tc.SelectedIndex == 3)
            {
                ScrollLogsToBottom();
            }
        }
    }

    private static void FormatLogLine(Paragraph p, string line, bool isLast)
    {
        var match = Regex.Match(line, @"^(\[\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}\])\s+(\[[A-Z]+\])\s*(.*)$");
        if (match.Success)
        {
            // Horodatage [YYYY-MM-DD HH:mm:ss]
            var timeRun = new Run(match.Groups[1].Value + " ")
            {
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isLast ? "#FACC15" : "#94A3B8")),
                FontWeight = isLast ? FontWeights.Bold : FontWeights.Normal
            };
            p.Inlines.Add(timeRun);

            // Niveau [INFO], [WARNING], [ERROR], [DEBUG]
            string level = match.Groups[2].Value.ToUpperInvariant();
            Color levelColor = isLast 
                ? (Color)ColorConverter.ConvertFromString("#FACC15")
                : level switch
                {
                    "[ERROR]" => (Color)ColorConverter.ConvertFromString("#EF4444"),
                    "[WARNING]" => (Color)ColorConverter.ConvertFromString("#F59E0B"),
                    "[DEBUG]" => (Color)ColorConverter.ConvertFromString("#94A3B8"),
                    _ => (Color)ColorConverter.ConvertFromString("#38BDF8")
                };

            var levelRun = new Run(match.Groups[2].Value + " ")
            {
                Foreground = new SolidColorBrush(levelColor),
                FontWeight = FontWeights.Bold
            };
            p.Inlines.Add(levelRun);

            // Message : Le dernier est jaune (#FACC15), les précédents deviennent blancs (#FFFFFF)
            string rawMsg = match.Groups[3].Value;
            string msg = LocalizationService.Instance.TranslateLogLine(rawMsg);
            SolidColorBrush msgBrush = isLast
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FACC15"))
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF"));

            var msgRun = new Run(msg)
            {
                Foreground = msgBrush,
                FontWeight = isLast ? FontWeights.Bold : FontWeights.Normal
            };
            p.Inlines.Add(msgRun);
        }
        else
        {
            Color fallbackColor = isLast 
                ? (Color)ColorConverter.ConvertFromString("#FACC15")
                : (Color)ColorConverter.ConvertFromString("#FFFFFF");

            string translatedLine = LocalizationService.Instance.TranslateLogLine(line);
            var rawRun = new Run(translatedLine)
            {
                Foreground = new SolidColorBrush(fallbackColor),
                FontWeight = isLast ? FontWeights.Bold : FontWeights.Normal
            };
            p.Inlines.Add(rawRun);
        }
    }

    private void OnSettingsScrollViewerPreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer scv)
        {
            double targetOffset = scv.VerticalOffset - (e.Delta * 0.35);
            if (targetOffset < 0) targetOffset = 0;
            if (targetOffset > scv.ScrollableHeight) targetOffset = scv.ScrollableHeight;
            scv.ScrollToVerticalOffset(targetOffset);
            e.Handled = true;
        }
    }

    private void OnLogRichTextBoxPreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (sender is RichTextBox rtb)
        {
            // Vitesse ralentie et fluide : 24 pixels par cran de molette (environ 1.5 ligne de log)
            double step = -(e.Delta / 120.0) * 24.0;
            double target = Math.Max(0, rtb.VerticalOffset + step);
            rtb.ScrollToVerticalOffset(target);
            e.Handled = true;
        }
    }
}
