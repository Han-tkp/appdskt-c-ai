using Avalonia.Controls;
using Avalonia.Input;
using DropDetect.ViewModels;

namespace DropDetect;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

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