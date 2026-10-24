using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace UberDownloader.Wpf.ViewModels;

public partial class SelectableItem : ObservableObject
{
    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _code = string.Empty;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isAuto;

    public Action? SelectionChanged { get; set; }

    partial void OnIsSelectedChanged(bool value)
    {
        SelectionChanged?.Invoke();
    }
}
