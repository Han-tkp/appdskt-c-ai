using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using DropDetect.ViewModels;

namespace DropDetect;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
