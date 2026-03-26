using LocalWealthTracker.Models;
using LocalWealthTracker.Services;
using System.Windows;
using System.Windows.Controls;

namespace LocalWealthTracker;

public partial class ModifierProfilesWindow : Window
{
    private readonly DataService _data;
    private List<ModifierProfile> _profiles = [];
    private ModifierProfile? _selected;
    private bool _loading;

    public ModifierProfilesWindow()
    {
        InitializeComponent();
        _data = new DataService();
        LoadProfiles();
    }

    private void LoadProfiles()
    {
        _profiles = _data.LoadSettings().ModifierProfiles;
        RefreshList();
    }

    private void RefreshList()
    {
        _loading = true;
        ProfileList.Items.Clear();
        foreach (var p in _profiles)
            ProfileList.Items.Add(p);
        ProfileList.DisplayMemberPath = "Name";
        _loading = false;
    }

    private void ProfileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _selected = ProfileList.SelectedItem as ModifierProfile;
        DeleteBtn.IsEnabled = _selected != null;

        if (_selected == null)
        {
            EditorPanel.Visibility = Visibility.Collapsed;
            NoSelectionHint.Visibility = Visibility.Visible;
            return;
        }

        _loading = true;
        NoSelectionHint.Visibility = Visibility.Collapsed;
        EditorPanel.Visibility = Visibility.Visible;
        NameBox.Text = _selected.Name;
        ModsBox.Text = string.Join("\n", _selected.Modifiers);
        _loading = false;
    }

    private void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        var profile = new ModifierProfile
        {
            Id   = Guid.NewGuid().ToString(),
            Name = "New Profile"
        };
        _profiles.Add(profile);
        RefreshList();
        ProfileList.SelectedItem = profile;
        NameBox.Focus();
        NameBox.SelectAll();
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null) return;

        var result = MessageBox.Show(
            $"Delete profile \"{_selected.Name}\"?\n\nTabs assigned to this profile will lose their assignment.",
            "Delete Profile",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result != MessageBoxResult.Yes) return;

        _profiles.Remove(_selected);
        _selected = null;
        RefreshList();
        EditorPanel.Visibility = Visibility.Collapsed;
        NoSelectionHint.Visibility = Visibility.Visible;
        DeleteBtn.IsEnabled = false;
    }

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _selected == null) return;
        _selected.Name = NameBox.Text;

        // Refresh list item display
        int idx = _profiles.IndexOf(_selected);
        if (idx >= 0)
        {
            _loading = true;
            ProfileList.Items.Clear();
            foreach (var p in _profiles)
                ProfileList.Items.Add(p);
            ProfileList.SelectedItem = _selected;
            _loading = false;
        }
    }

    private void ModsBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _selected == null) return;
        _selected.Modifiers = ModsBox.Text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();
    }

    private void SaveClose_Click(object sender, RoutedEventArgs e)
    {
        var s = _data.LoadSettings();

        // Clean up tab assignments for deleted profiles
        var validIds = _profiles.Select(p => p.Id).ToHashSet();
        foreach (var tab in s.Tabs)
        {
            if (tab.ModifierProfileId != null && !validIds.Contains(tab.ModifierProfileId))
                tab.ModifierProfileId = null;
        }

        s.ModifierProfiles = _profiles;
        _data.SaveSettings(s);
        StatusText.Text = "Saved.";
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
