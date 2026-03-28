using LocalWealthTracker.Models;
using LocalWealthTracker.Services;
using System.Windows;
using System.Windows.Controls;

namespace LocalWealthTracker;

public partial class ModifierProfilesWindow : Window
{
    private readonly DataService _data;
    private readonly ModDataService _modData = new();

    private List<ModifierProfile> _profiles = [];
    private List<TradeStatEntry> _allMods = [];
    private List<TradeItemEntry> _allBaseTypes = [];

    private ModifierProfile? _selected;
    private bool _loading;

    public ModifierProfilesWindow()
    {
        InitializeComponent();
        _data = new DataService();
        LoadProfiles();
        Loaded += async (_, _) =>
        {
            await Task.WhenAll(
                LoadModsAsync(false),
                LoadBaseTypesAsync());
        };
    }

    // ── Mod data loading ────────────────────────────────────────────

    private async Task LoadModsAsync(bool forceRefresh)
    {
        SetModLoadingState(loading: true);

        var (mods, categories, error) = forceRefresh
            ? await _modData.ReloadModsAsync()
            : await _modData.LoadModsAsync();

        _allMods = mods;

        _loading = true;
        CategoryCombo.Items.Clear();
        foreach (var cat in categories)
            CategoryCombo.Items.Add(cat);
        CategoryCombo.SelectedIndex = 0;
        _loading = false;

        if (error != null)
        {
            LoadingRow.Visibility = Visibility.Collapsed;
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            ResultCountText.Visibility = Visibility.Collapsed;
        }
        else
        {
            LoadingRow.Visibility = Visibility.Collapsed;
            ErrorText.Visibility = Visibility.Collapsed;
            FilterMods();
        }
    }

    private async Task LoadBaseTypesAsync()
    {
        var (items, categories, _) = await _modData.LoadBaseTypesAsync();
        _allBaseTypes = items;

        _loading = true;
        BaseGroupCombo.Items.Clear();
        foreach (var cat in categories)
            BaseGroupCombo.Items.Add(cat);
        BaseGroupCombo.SelectedIndex = 0;   // "Any Base"
        _loading = false;

        PopulateBaseTypeCombo("Any Base");

        // Attach live search to the editable text box once the template is ready
        BaseTypeCombo.Loaded += (_, _) => AttachBaseTypeSearch();
        if (BaseTypeCombo.IsLoaded) AttachBaseTypeSearch();
    }

    private void AttachBaseTypeSearch()
    {
        if (BaseTypeCombo.Template.FindName("PART_EditableTextBox", BaseTypeCombo)
            is not TextBox tb) return;

        tb.TextChanged += (_, _) =>
        {
            if (_loading) return;
            var search = tb.Text;
            RebuildBaseTypeItems(search);
            // Keep dropdown open and restore cursor position
            BaseTypeCombo.IsDropDownOpen = true;
            tb.CaretIndex = search.Length;
        };
    }

    private void PopulateBaseTypeCombo(string groupLabel)
    {
        _loading = true;
        BaseTypeCombo.Items.Clear();
        BaseTypeCombo.Items.Add("Any Base");

        if (groupLabel != "Any Base")
        {
            foreach (var entry in _allBaseTypes.Where(
                e => e.GroupLabel.Equals(groupLabel, StringComparison.OrdinalIgnoreCase)))
                BaseTypeCombo.Items.Add(entry.Type);
        }

        BaseTypeCombo.Text = "Any Base";
        _loading = false;
    }

    private void RebuildBaseTypeItems(string search)
    {
        var group = BaseGroupCombo.SelectedItem as string ?? "Any Base";

        var entries = _allBaseTypes.AsEnumerable();
        if (group != "Any Base")
            entries = entries.Where(e => e.GroupLabel.Equals(group, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(search) && search != "Any Base")
            entries = entries.Where(e => e.Type.Contains(search, StringComparison.OrdinalIgnoreCase));

        _loading = true;
        BaseTypeCombo.Items.Clear();
        BaseTypeCombo.Items.Add("Any Base");
        foreach (var e in entries)
            BaseTypeCombo.Items.Add(e.Type);
        _loading = false;
    }

    private void SetModLoadingState(bool loading)
    {
        LoadingRow.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        ErrorText.Visibility = Visibility.Collapsed;
        if (loading) ResultCountText.Visibility = Visibility.Collapsed;
        ModResultsList.ItemsSource = null;
    }

    private void FilterMods()
    {
        if (_allMods.Count == 0) return;

        var category = CategoryCombo.SelectedItem as string;
        var search = SearchBox.Text.Trim();

        var filtered = _allMods.AsEnumerable();

        if (!string.IsNullOrEmpty(category) && category != "All")
            filtered = filtered.Where(m => m.GroupLabel.Equals(category, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(search))
            filtered = filtered.Where(m => m.Text.Contains(search, StringComparison.OrdinalIgnoreCase));

        var results = filtered.Take(100).ToList();
        ModResultsList.ItemsSource = results;

        int total = filtered.Count();
        ResultCountText.Text = total > 100
            ? $"Showing 100 of {total} results — refine your search"
            : $"{total} result{(total == 1 ? "" : "s")}";
        ResultCountText.Visibility = Visibility.Visible;
    }

    // ── Event handlers: mod finder ───────────────────────────────────

    private void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _allMods.Count == 0) return;
        FilterMods();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _allMods.Count == 0) return;
        FilterMods();
    }

    private void BaseGroupCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        var group = BaseGroupCombo.SelectedItem as string ?? "Any Base";
        PopulateBaseTypeCombo(group);
        // Clear any previous search text
        BaseTypeCombo.Text = "Any Base";
    }

    private void BaseTypeCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        // No action needed — selected value is read when the user clicks "+"
    }

    private async void RefreshMods_Click(object sender, RoutedEventArgs e)
    {
        await LoadModsAsync(forceRefresh: true);
    }

    private void AddMod_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null) return;
        if ((sender as Button)?.DataContext is not TradeStatEntry entry) return;

        var rawBase  = BaseTypeCombo.Text?.Trim();
        var rawGroup = BaseGroupCombo.SelectedItem as string;

        string? baseType      = null;
        List<string>? groupList = null;
        string? groupLabel    = null;

        if (!string.IsNullOrEmpty(rawBase) && rawBase != "Any Base")
        {
            // Specific base type chosen
            baseType = rawBase;
        }
        else if (!string.IsNullOrEmpty(rawGroup) && rawGroup != "Any Base")
        {
            // "Any Base" within a specific group — snapshot the group's base types now
            groupList = _allBaseTypes
                .Where(b => b.GroupLabel.Equals(rawGroup, StringComparison.OrdinalIgnoreCase))
                .Select(b => b.Type)
                .ToList();
            groupLabel = rawGroup;
        }
        // else: no base constraint at all

        bool duplicate = _selected.Modifiers.Any(m =>
            m.ModText.Equals(entry.Text, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(m.BaseType, baseType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(m.BaseGroupLabel, groupLabel, StringComparison.OrdinalIgnoreCase));

        if (!duplicate)
        {
            _selected.Modifiers.Add(new ProfileMod
            {
                ModText       = entry.Text,
                BaseType      = baseType,
                BaseTypeGroup = groupList,
                BaseGroupLabel = groupLabel
            });
            RefreshProfileModsList();
        }
    }

    private void RemoveMod_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null) return;
        if ((sender as Button)?.DataContext is not ProfileMod mod) return;

        _selected.Modifiers.Remove(mod);
        RefreshProfileModsList();
    }

    private void RefreshProfileModsList()
    {
        ProfileModsList.ItemsSource = null;
        ProfileModsList.ItemsSource = _selected?.Modifiers ?? [];
        ProfileModsHeader.Text = $"PROFILE MODIFIERS ({_selected?.Modifiers.Count ?? 0})";
    }

    // ── Profile list management ──────────────────────────────────────

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
        _loading = false;

        RefreshProfileModsList();
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

        _loading = true;
        ProfileList.Items.Clear();
        foreach (var p in _profiles)
            ProfileList.Items.Add(p);
        ProfileList.SelectedItem = _selected;
        _loading = false;
    }

    // ── Save / close ─────────────────────────────────────────────────

    private void SaveClose_Click(object sender, RoutedEventArgs e)
    {
        var s = _data.LoadSettings();

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
