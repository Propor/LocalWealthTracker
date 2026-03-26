using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalWealthTracker.Models;
using LocalWealthTracker.Services;

namespace LocalWealthTracker.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly StashService _stash;
    private readonly DataService _data;

    [ObservableProperty] private string _sessionId = "";
    [ObservableProperty] private string _league = "";
    [ObservableProperty] private double _minItemValue = 1.0;
    [ObservableProperty] private int _autoRefreshMinutes;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasStoredCredential;
    [ObservableProperty] private int _priceCacheMinutes;
    [ObservableProperty] private double _divineGoal;


    public ObservableCollection<SelectableTab> Tabs { get; } = [];

    public SettingsViewModel()
    {
        _stash = new StashService();
        _data = new DataService();
        LoadSettings();
    }

    // ── Fetch stash tab list ────────────────────────────────────

    [RelayCommand]
    private async Task FetchStashesAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(SessionId) ||
            string.IsNullOrWhiteSpace(League))
        {
            StatusText = "⚠ Enter both POESESSID and league name first.";
            return;
        }

        IsBusy = true;
        StatusText = "Fetching stash tabs…";

        try
        {
            _stash.SetSessionId(SessionId.Trim());
            var (tabs, error) = await _stash.GetTabListAsync(League.Trim(), ct);

            if (error != null)
            {
                StatusText = $"❌ {error}";
                return;
            }

            if (tabs is null or { Count: 0 })
            {
                StatusText = "No stash tabs found.";
                return;
            }

            var previouslySynced = Tabs
                .Where(t => t.IsSynced)
                .Select(t => t.Index)
                .ToHashSet();

            Tabs.Clear();

            foreach (var tab in tabs)
            {
                if (tab.Hidden) continue;

                var color = tab.Colour != null
                    ? Color.FromRgb((byte)tab.Colour.R,
                                    (byte)tab.Colour.G,
                                    (byte)tab.Colour.B)
                    : Color.FromRgb(128, 128, 128);

                Tabs.Add(new SelectableTab
                {
                    Index = tab.Index,
                    Name = tab.Name,
                    Type = tab.Type,
                    Color = color,
                    IsSynced = previouslySynced.Contains(tab.Index)
                });
            }

            StatusText = $"✅ Found {Tabs.Count} tabs. Check the ones you want to sync.";
        }
        catch (Exception ex)
        {
            StatusText = $"❌ {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ── Select all / none ───────────────────────────────────────

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var tab in Tabs) tab.IsSynced = true;
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var tab in Tabs) tab.IsSynced = false;
    }

    // ── Delete stored credential ────────────────────────────────

    [RelayCommand]
    private void DeleteCredential()
    {
        CredentialService.Delete();
        SessionId = "";
        HasStoredCredential = false;
        StatusText = "🗑 Stored POESESSID deleted.";
    }

    // ── Save & Load ─────────────────────────────────────────────

    [RelayCommand]
    private void Save()
    {
        if (!string.IsNullOrWhiteSpace(SessionId))
        {
            CredentialService.Save(SessionId.Trim());
            HasStoredCredential = true;
        }

        var existing = _data.LoadSettings();
        // Build a quick lookup to preserve ModifierProfileId per tab index
        var existingTabMap = existing.Tabs.ToDictionary(t => t.Index);

        _data.SaveSettings(new AppSettings
        {
            League = League,
            MinItemValueChaos = MinItemValue,
            AutoRefreshMinutes = AutoRefreshMinutes,
            PriceCacheMinutes = PriceCacheMinutes,
            DivineGoal = DivineGoal,
            TabOrder = existing.TabOrder,
            ModifierProfiles = existing.ModifierProfiles,   // preserve profiles
            Tabs = Tabs.Select(t => new SavedTab
            {
                Index = t.Index,
                Name = t.Name,
                Type = t.Type,
                ColorR = t.Color.R,
                ColorG = t.Color.G,
                ColorB = t.Color.B,
                IsSynced = t.IsSynced,
                // Preserve existing modifier profile assignment for each tab
                ModifierProfileId = existingTabMap.TryGetValue(t.Index, out var et)
                    ? et.ModifierProfileId : null
            }).ToList()
        });

        StatusText = "✅ Settings saved. POESESSID encrypted with DPAPI.";
    }

    private void LoadSettings()
    {
        var storedSession = CredentialService.Load();
        if (storedSession != null)
        {
            SessionId = storedSession;
            HasStoredCredential = true;
        }

        var s = _data.LoadSettings();
        League = s.League;
        MinItemValue = s.MinItemValueChaos;
        AutoRefreshMinutes = s.AutoRefreshMinutes;
        PriceCacheMinutes = s.PriceCacheMinutes;
        DivineGoal = s.DivineGoal;

        Tabs.Clear();
        foreach (var saved in s.Tabs)
        {
            Tabs.Add(new SelectableTab
            {
                Index = saved.Index,
                Name = saved.Name,
                Type = saved.Type,
                Color = Color.FromRgb(
                    (byte)saved.ColorR,
                    (byte)saved.ColorG,
                    (byte)saved.ColorB),
                IsSynced = saved.IsSynced
            });
        }

        if (Tabs.Count > 0)
            StatusText = $"{Tabs.Count} tabs loaded. {Tabs.Count(t => t.IsSynced)} synced.";
    }
}