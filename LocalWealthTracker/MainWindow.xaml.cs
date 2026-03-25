using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LocalWealthTracker.Models;
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

    // ── Title bar ────────────────────────────────────────────────

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => Close();

    // ── Tab drag-and-drop ────────────────────────────────────────

    private Point _tabDragStart;
    private TabSummary? _draggedTab;

    private void TabsList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _tabDragStart = e.GetPosition(null);
    }

    private void TabsList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _draggedTab != null)
            return;

        var pos = e.GetPosition(null);
        var diff = _tabDragStart - pos;

        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var item = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
        if (item?.DataContext is not TabSummary tab) return;

        // "All Items" aggregate always stays at the top — not draggable
        if (tab.Index == -1) return;

        _draggedTab = tab;
        DragDrop.DoDragDrop(item, tab, DragDropEffects.Move);
        _draggedTab = null;
    }

    private void TabsList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(TabSummary))
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void TabsList_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(TabSummary))) return;

        var dropped = (TabSummary)e.Data.GetData(typeof(TabSummary));
        var target = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
        if (target?.DataContext is not TabSummary targetTab) return;
        if (targetTab == dropped) return;

        // Don't allow dropping onto or above "All Items"
        if (targetTab.Index == -1) return;

        var vm = (MainViewModel)DataContext;
        vm.MoveTab(vm.Tabs.IndexOf(dropped), vm.Tabs.IndexOf(targetTab));
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T t) return t;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}