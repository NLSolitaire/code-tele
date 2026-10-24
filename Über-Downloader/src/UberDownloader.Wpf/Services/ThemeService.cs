using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using UberDownloader.Core.Services;

namespace UberDownloader.Wpf.Services;

public static class ThemeService
{
    private static string _currentMode = "Dark";
    private static bool _listenerAttached;

    public static void Initialize(string initialMode)
    {
        if (!_listenerAttached)
        {
            try
            {
                SystemEvents.UserPreferenceChanged += (s, e) =>
                {
                    if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.VisualStyle)
                    {
                        if (string.Equals(_currentMode, "System", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(_currentMode, "Système", StringComparison.OrdinalIgnoreCase))
                        {
                            Application.Current.Dispatcher.Invoke(() => ApplyTheme("System"));
                        }
                    }
                };
                _listenerAttached = true;
            }
            catch { }
        }

        ApplyTheme(initialMode);
    }

    public static void ApplyTheme(string themeChoice)
    {
        _currentMode = themeChoice;
        bool isDark = true;

        if (string.Equals(themeChoice, "Light", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(themeChoice, "Jour", StringComparison.OrdinalIgnoreCase) ||
            themeChoice.Contains("Jour", StringComparison.OrdinalIgnoreCase))
        {
            isDark = false;
        }
        else if (string.Equals(themeChoice, "Dark", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(themeChoice, "Nuit", StringComparison.OrdinalIgnoreCase) ||
                 themeChoice.Contains("Nuit", StringComparison.OrdinalIgnoreCase))
        {
            isDark = true;
        }
        else // System / Système
        {
            isDark = IsSystemDark();
        }

        ApplyColors(isDark);
    }

    private static bool IsSystemDark()
    {
        try
        {
            object? res = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
            if (res is int val)
            {
                return val == 0;
            }
        }
        catch { }
        return true;
    }

    private static void ApplyColors(bool isDark)
    {
        var dict = Application.Current?.Resources;
        if (dict == null) return;

        if (isDark)
        {
            dict["WindowBackgroundBrush"] = CreateBrush("#0F172A");
            dict["CardBackgroundBrush"] = CreateBrush("#1E293B");
            dict["SubCardBackgroundBrush"] = CreateBrush("#111827");
            dict["InputBackgroundBrush"] = CreateBrush("#0F172A");
            dict["BorderBrushColor"] = CreateBrush("#334155");
            dict["TextPrimaryBrush"] = CreateBrush("#F8FAFC");
            dict["TextSecondaryBrush"] = CreateBrush("#94A3B8");
            dict["TextMutedBrush"] = CreateBrush("#64748B");
            dict["CardItemBorderBrush"] = CreateBrush("#2D3A5D");
            dict["LogViewerBackgroundBrush"] = CreateBrush("#0A0F1D");
            dict["DropDownBackgroundBrush"] = CreateBrush("#0F172A");
            dict["TabBackgroundBrush"] = CreateBrush("#1E293B");
            dict["TabSelectedBackgroundBrush"] = CreateBrush("#0F172A");
            dict["TabHeaderForegroundBrush"] = CreateBrush("#94A3B8");
            dict["TabSelectedForegroundBrush"] = CreateBrush("#38BDF8");
            dict["BadgeButtonBackgroundBrush"] = CreateBrush("#334155");
            dict["BadgeButtonForegroundBrush"] = CreateBrush("#F8FAFC");
        }
        else
        {
            dict["WindowBackgroundBrush"] = CreateBrush("#F1F5F9");
            dict["CardBackgroundBrush"] = CreateBrush("#FFFFFF");
            dict["SubCardBackgroundBrush"] = CreateBrush("#F8FAFC");
            dict["InputBackgroundBrush"] = CreateBrush("#F1F5F9");
            dict["BorderBrushColor"] = CreateBrush("#CBD5E1");
            dict["TextPrimaryBrush"] = CreateBrush("#0F172A");
            dict["TextSecondaryBrush"] = CreateBrush("#334155");
            dict["TextMutedBrush"] = CreateBrush("#64748B");
            dict["CardItemBorderBrush"] = CreateBrush("#E2E8F0");
            dict["LogViewerBackgroundBrush"] = CreateBrush("#FFFFFF");
            dict["DropDownBackgroundBrush"] = CreateBrush("#FFFFFF");
            dict["TabBackgroundBrush"] = CreateBrush("#E2E8F0");
            dict["TabSelectedBackgroundBrush"] = CreateBrush("#2563EB");
            dict["TabHeaderForegroundBrush"] = CreateBrush("#1E293B");
            dict["TabSelectedForegroundBrush"] = CreateBrush("#FFFFFF");
            dict["BadgeButtonBackgroundBrush"] = CreateBrush("#E2E8F0");
            dict["BadgeButtonForegroundBrush"] = CreateBrush("#0F172A");
        }
    }

    private static SolidColorBrush CreateBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
