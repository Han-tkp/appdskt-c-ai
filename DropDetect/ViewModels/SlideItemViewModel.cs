using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DropDetect.ViewModels;

public partial class SlideItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _slideName = string.Empty;

    [ObservableProperty]
    private string _statusText = "⏳ Pending";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusColor))]
    private bool _isAnalyzed = false;

    [ObservableProperty]
    private int _dropletsCaptured = 0;

    public List<double> CapturedDroplets { get; set; } = new();

    public string StatusColor => IsAnalyzed ? "#A6E3A1" : "#A6ADC8";
}
