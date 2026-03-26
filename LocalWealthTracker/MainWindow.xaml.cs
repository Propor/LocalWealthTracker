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

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var source = System.Windows.Interop.HwndSource.FromHwnd(
            new System.Windows.Interop.WindowInteropHelper(this).Handle);
        source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_GETMINMAXINFO = 0x0024;
        if (msg == WM_GETMINMAXINFO)
        {
            ApplyMaximizeBounds(hwnd, lParam);
            handled = true;
        }
        return IntPtr.Zero;
    }

    [System.Runtime.InteropServices.DllImport("user32")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, MonitorInfo lpmi);

    [System.Runtime.InteropServices.DllImport("user32")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativePoint { public int x, y; }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private class MonitorInfo
    {
        public int cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(MonitorInfo));
        public NativeRect rcMonitor, rcWork;
        public int dwFlags;
    }

    private static void ApplyMaximizeBounds(IntPtr hwnd, IntPtr lParam)
    {
        var mmi = System.Runtime.InteropServices.Marshal.PtrToStructure<MinMaxInfo>(lParam);
        var monitor = MonitorFromWindow(hwnd, 0x00000002);
        if (monitor != IntPtr.Zero)
        {
            var info = new MonitorInfo();
            GetMonitorInfo(monitor, info);
            mmi.ptMaxPosition.x = info.rcWork.Left - info.rcMonitor.Left;
            mmi.ptMaxPosition.y = info.rcWork.Top  - info.rcMonitor.Top;
            mmi.ptMaxSize.x     = info.rcWork.Right  - info.rcWork.Left;
            mmi.ptMaxSize.y     = info.rcWork.Bottom - info.rcWork.Top;
            mmi.ptMaxTrackSize  = mmi.ptMaxSize;
        }
        System.Runtime.InteropServices.Marshal.StructureToPtr(mmi, lParam, true);
    }

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

    // ── Tab right-click context menu ─────────────────────────────

    private void TabsList_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
        if (item?.DataContext is not TabSummary tab) return;
        if (tab.Index == -1) return; // "All Items" aggregate — no profile assignment

        var vm = (MainViewModel)DataContext;
        var menu = new ContextMenu
        {
            Background  = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x18, 0x18, 0x1b)),
            BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x3f, 0x3f, 0x46)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0, 4, 0, 4)
        };

        // "Assign profile" header item (disabled, acts as label)
        var assignHeader = BuildMenuItem("Assign Modifier Profile", null, isHeader: true);
        menu.Items.Add(assignHeader);

        if (vm.ModifierProfiles.Count == 0)
        {
            var noProfiles = BuildMenuItem("  No profiles — click Manage Profiles…", null, isHeader: false);
            noProfiles.IsEnabled = false;
            menu.Items.Add(noProfiles);
        }
        else
        {
            foreach (var profile in vm.ModifierProfiles)
            {
                var profileItem = BuildMenuItem($"  {profile.Name}", null, isHeader: false);
                profileItem.IsChecked = tab.ModifierProfileId == profile.Id;
                var profileId = profile.Id;
                profileItem.Click += (_, _) => vm.SetTabModifierProfile(tab, profileId);
                menu.Items.Add(profileItem);
            }
        }

        if (tab.ModifierProfileId != null)
        {
            menu.Items.Add(new Separator());
            var clearItem = BuildMenuItem("✕  Clear Modifier Profile", null, isHeader: false);
            clearItem.Click += (_, _) => vm.SetTabModifierProfile(tab, null);
            menu.Items.Add(clearItem);
        }

        menu.Items.Add(new Separator());
        var manageItem = BuildMenuItem("⚙  Manage Profiles…", null, isHeader: false);
        manageItem.Click += (_, _) => OpenModifierProfilesWindow();
        menu.Items.Add(manageItem);

        menu.IsOpen = true;
        e.Handled   = true;
    }

    private static MenuItem BuildMenuItem(string header, object? icon, bool isHeader)
    {
        var item = new MenuItem
        {
            Header     = header,
            Foreground = isHeader
                ? new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x71, 0x71, 0x7a))
                : new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xf4, 0xf4, 0xf5)),
            Background  = System.Windows.Media.Brushes.Transparent,
            IsEnabled   = !isHeader,
            FontFamily  = new System.Windows.Media.FontFamily("Segoe UI"),
            FontSize    = 13,
            Padding     = new Thickness(12, 4, 12, 4)
        };
        return item;
    }

    private void OpenModifierProfilesWindow()
    {
        var win = new ModifierProfilesWindow { Owner = this };
        win.ShowDialog();
        // Reload profiles into ViewModel after the dialog closes
        ((MainViewModel)DataContext).LoadFromSettings();
    }
}