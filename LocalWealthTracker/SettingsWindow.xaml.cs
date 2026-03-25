using System.Windows;
using System.Windows.Controls;
using LocalWealthTracker.ViewModels;

namespace LocalWealthTracker;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;

    public SettingsWindow()
    {
        InitializeComponent();
        _vm = new SettingsViewModel();
        DataContext = _vm;

        Loaded += (_, _) =>
        {
            if (!string.IsNullOrEmpty(_vm.SessionId))
                SessionBox.Password = _vm.SessionId;
        };
    }

    private void SessionBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox pb)
            _vm.SessionId = pb.Password;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void TitleBarClose_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}