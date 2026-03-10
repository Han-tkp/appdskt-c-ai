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
        // Tunnel = intercept BEFORE child controls (e.g. Button) receive the event
        this.AddHandler(InputElement.KeyDownEvent, MainWindow_KeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        // ⚠️ Avalonia Button activate บน KeyUp ไม่ใช่ KeyDown → ต้องกัน KeyUp ด้วย
        this.AddHandler(InputElement.KeyUpEvent, MainWindow_KeyUp, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        this.DataContextChanged += MainWindow_DataContextChanged;
        this.Loaded += MainWindow_Loaded;
        this.Closing += MainWindow_Closing;
    }

    private async void MainWindow_Loaded(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            if (vm.AutoSaveService.GetLastAutoSaveFilePath() != null)
            {
                var dialog = new Window()
                {
                    Title = "Auto-Save Recovery",
                    Width = 450,
                    Height = 150,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Background = Avalonia.Media.Brushes.DarkSlateBlue,
                    CanResize = false
                };

                var panel = new StackPanel { Spacing = 20, Margin = new Avalonia.Thickness(20) };
                panel.Children.Add(new TextBlock
                {
                    Text = "An unsaved session was found from a previous run.\nWould you like to recover it?",
                    Foreground = Avalonia.Media.Brushes.White,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    FontWeight = Avalonia.Media.FontWeight.Bold,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    TextAlignment = Avalonia.Media.TextAlignment.Center
                });

                var btnPanel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Spacing = 20 };

                var btnYes = new Button { Content = "Yes, Recover", Width = 120, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center, Background = Avalonia.Media.Brushes.Green, Foreground = Avalonia.Media.Brushes.White };
                btnYes.Click += async (s, args) =>
                {
                    dialog.Close();
                    await vm.RecoverAutoSaveAsync();
                };

                var btnNo = new Button { Content = "No, Delete it", Width = 120, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center };
                btnNo.Click += (s, args) =>
                {
                    vm.AutoSaveService.ClearAutoSave();
                    dialog.Close();
                };

                btnPanel.Children.Add(btnYes);
                btnPanel.Children.Add(btnNo);
                panel.Children.Add(btnPanel);

                dialog.Content = panel;
                await dialog.ShowDialog(this);
            }
        }
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
        if (DataContext is not MainWindowViewModel vm) return;

        bool isSnapshotKey = e.Key == vm.SnapshotHotkey;
        bool isLiveAiKey = e.Key == vm.LiveAiHotkey;

        // ถ้าคีย์ตรงกับ hotkey ใดๆ → กิน event ทันที ไม่ให้กระเด็นไปกดปุ่มที่ focused
        if (isSnapshotKey || isLiveAiKey)
        {
            e.Handled = true;

            // ไม่ execute action ถ้า: Settings เปิดอยู่ หรือ กำลัง assign hotkey ใหม่
            if (_settingsWindow != null) return;
            if (vm.IsListeningForSnapshotHotkey || vm.IsListeningForLiveAiHotkey) return;

            if (isSnapshotKey)
                vm.TakeSnapshotCommand();
            else
                vm.AnalyzeCommand.Execute(null);
        }
    }

    /// <summary>
    /// กัน KeyUp ของ hotkey ด้วย เพราะ Avalonia Button activate ผ่าน KeyUp ไม่ใช่ KeyDown
    /// </summary>
    private void MainWindow_KeyUp(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        // กิน KeyUp ของ hotkey เพื่อป้องกัน Button ที่ focused ถูกกระตุ้น
        // ไม่ execute action ใดๆ ที่นี่ — action ถูก fire ไปแล้วใน KeyDown
        if (e.Key == vm.SnapshotHotkey || e.Key == vm.LiveAiHotkey)
            e.Handled = true;
    }

    private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            if (vm.ReportData.Count > 0 && !_isConfirmedExit && vm.HasUnsavedChanges)
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
        else
        {
            // Clean exit or confirmed exit
            vm.AutoSaveService.ClearAutoSave();
        }
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
    private void ExitApp_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        this.Close();
    }
}
