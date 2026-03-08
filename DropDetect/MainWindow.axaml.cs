using Avalonia.Controls;
using Avalonia.Input;
using DropDetect.ViewModels;

namespace DropDetect;

public partial class MainWindow : Window
{
    private SettingsWindow? _settingsWindow;

    public MainWindow()
    {
        InitializeComponent();
        this.KeyDown += MainWindow_KeyDown;
        this.DataContextChanged += MainWindow_DataContextChanged;
        this.Closing += MainWindow_Closing;
    }

    private void MainWindow_DataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.ShowSettingsRequested = () =>
            {
                if (_settingsWindow == null)
                {
                    _settingsWindow = new SettingsWindow
                    {
                        DataContext = vm // Pass the same ViewModel
                    };
                    _settingsWindow.Closed += (s, args) => _settingsWindow = null;
                    _settingsWindow.Show(this);
                }
                else
                {
                    _settingsWindow.Activate();
                }
            };

            vm.CloseSettingsRequested = () =>
            {
                _settingsWindow?.Close();
            };
        }
    }

    private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            if (System.Enum.TryParse<Key>(vm.SnapshotHotkey, true, out var hotkey))
            {
                if (e.Key == hotkey && hotkey != Key.None)
                {
                    vm.TakeSnapshotCommand();
                    e.Handled = true; // Prevent hotkey from scrolling or clicking focused buttons accidentally
                }
            }
        }
    }

    private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && vm.ReportData.Count > 0 && !_isConfirmedExit)
        {
            e.Cancel = true; // Stop the window from closing immediately

            var dialog = new Window()
            {
                Title = "Unsaved Data Warning",
                Width = 400,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Avalonia.Media.Brushes.DarkRed,
                CanResize = false
            };

            var panel = new StackPanel { Spacing = 20, Margin = new Avalonia.Thickness(20) };
            panel.Children.Add(new TextBlock
            {
                Text = $"You have {vm.ReportData.Count} unsaved report(s)!\nAre you sure you want to exit without exporting?",
                Foreground = Avalonia.Media.Brushes.White,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                FontWeight = Avalonia.Media.FontWeight.Bold,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                TextAlignment = Avalonia.Media.TextAlignment.Center
            });

            var btnPanel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Spacing = 20 };

            var btnYes = new Button { Content = "Yes, Exit", Width = 100, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center, Background = Avalonia.Media.Brushes.Black, Foreground = Avalonia.Media.Brushes.White };
            btnYes.Click += (s, args) =>
            {
                _isConfirmedExit = true;
                dialog.Close();
                this.Close(); // Actually exit
            };

            var btnNo = new Button { Content = "No, Cancel", Width = 100, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center };
            btnNo.Click += (s, args) => dialog.Close();

            btnPanel.Children.Add(btnYes);
            btnPanel.Children.Add(btnNo);
            panel.Children.Add(btnPanel);

            dialog.Content = panel;
            await dialog.ShowDialog(this);
        }
    }

    private bool _isConfirmedExit = false;

    private void CameraImage_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var image = sender as Image;
        if (image == null || image.Source == null) return;

        var currentPoint = e.GetCurrentPoint(image);
        if (!currentPoint.Properties.IsLeftButtonPressed) return;

        var point = currentPoint.Position;

        double controlWidth = image.Bounds.Width;
        double controlHeight = image.Bounds.Height;

        double sourceWidth = image.Source.Size.Width;
        double sourceHeight = image.Source.Size.Height;

        // Uniform stretch logic
        double scaleX = controlWidth / sourceWidth;
        double scaleY = controlHeight / sourceHeight;
        double scale = System.Math.Min(scaleX, scaleY);

        double displayedWidth = sourceWidth * scale;
        double displayedHeight = sourceHeight * scale;

        double offsetX = (controlWidth - displayedWidth) / 2.0;
        double offsetY = (controlHeight - displayedHeight) / 2.0;

        double imageX = (point.X - offsetX) / scale;
        double imageY = (point.Y - offsetY) / scale;

        if (imageX >= 0 && imageY >= 0 && imageX <= sourceWidth && imageY <= sourceHeight)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.ToggleIgnoreDroplet(imageX, imageY);
            }
        }
    }
}