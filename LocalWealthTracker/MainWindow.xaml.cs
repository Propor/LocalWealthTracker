using System.Windows;
using System.Windows.Input;
using LocalWealthTracker.ViewModels;

namespace LocalWealthTracker;

public partial class MainWindow : Window
{
    private double _zoomLevel = 1.0;
    private const double ZoomMin = 0.5;
    private const double ZoomMax = 3.0;
    private const double ZoomStep = 0.1;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    private void ItemsGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control)
            return;

        e.Handled = true;

        if (e.Delta > 0 && _zoomLevel < ZoomMax)
            _zoomLevel += ZoomStep;
        else if (e.Delta < 0 && _zoomLevel > ZoomMin)
            _zoomLevel -= ZoomStep;

        _zoomLevel = Math.Round(_zoomLevel, 1);

        GridScale.ScaleX = _zoomLevel;
        GridScale.ScaleY = _zoomLevel;
        ZoomLabel.Text = $"{_zoomLevel * 100:N0}%";
    }
}