using Avalonia.Controls;
using Avalonia.Input;
using DropDetect.ViewModels;

namespace DropDetect;

public partial class SettingsWindow : Window
{
    private bool _forceClose = false;

    public SettingsWindow()
    {
        InitializeComponent();
        this.Closing += SettingsWindow_Closing;
        this.AddHandler(InputElement.KeyDownEvent, SettingsWindow_OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private async void SettingsWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_forceClose) return;

        if (DataContext is MainWindowViewModel vm && vm.HasUnsavedChanges)
        {
            e.Cancel = true;

            var dialog = new Window()
            {
                Title = "Unsaved Changes",
                Width = 350, Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false
            };

            var panel = new StackPanel { Spacing = 20, Margin = new Avalonia.Thickness(20) };
            panel.Children.Add(new TextBlock { Text = "You have unsaved settings.\nDo you want to save them before closing?", TextWrapping = Avalonia.Media.TextWrapping.Wrap, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, TextAlignment = Avalonia.Media.TextAlignment.Center });

            var btnPanel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Spacing = 15 };
            
            var btnSave = new Button { Content = "Save", Width = 80, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center, Classes = { "accent" } };
            btnSave.Click += (s, args) => { vm.ApplySettingsCommand.Execute(null); _forceClose = true; dialog.Close(); this.Close(); };
            
            var btnDiscard = new Button { Content = "Discard", Width = 80, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center };
            btnDiscard.Click += (s, args) => { _forceClose = true; dialog.Close(); this.Close(); };

            var btnCancel = new Button { Content = "Cancel", Width = 80, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center };
            btnCancel.Click += (s, args) => { dialog.Close(); };

            btnPanel.Children.Add(btnSave);
            btnPanel.Children.Add(btnDiscard);
            btnPanel.Children.Add(btnCancel);
            panel.Children.Add(btnPanel);

            dialog.Content = panel;
            await dialog.ShowDialog(this);
        }
    }

    private void SettingsWindow_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            if (vm.IsListeningForSnapshotHotkey || vm.IsListeningForLiveAiHotkey)
            {
                vm.UpdateHotkey(e.Key);
                e.Handled = true;
            }
        }
    }
}
