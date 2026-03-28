using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LocalWealthTracker.Helpers;
using LocalWealthTracker.Models;
using LocalWealthTracker.Services;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;


namespace LocalWealthTracker.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly PriceService _prices;
    private readonly StashService _stash;
    private readonly PriceResolver _resolver;
    private readonly DataService _data;
    private readonly DispatcherTimer _autoTimer;
    private bool _updatingSelection;

    private List<PricedItem> _allCurrentItems = [];

    // ── UI properties ───────────────────────────────────────────
    [ObservableProperty]
    private string _statusText =
        "Open Settings to configure, then click Refresh.";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private int _refreshProgress;
    [ObservableProperty] private int _refreshTotal = 1;
    [ObservableProperty] private double _totalChaos;
    [ObservableProperty] private double _totalDivine;
    [ObservableProperty] private double _divinePrice;
    [ObservableProperty] private int _priceEntries;
    [ObservableProperty] private int _syncedTabCount;
    [ObservableProperty] private int _unpricedCount;
    [ObservableProperty] private string _lastRefresh = "—";
    [ObservableProperty] private string _leagueDisplay = "";
    [ObservableProperty] private string _itemsHeader = "(select a tab or snapshot)";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _itemCountText = "";
    [ObservableProperty] private bool _showUnpricedTab;
    [ObservableProperty] private TabSummary? _selectedTab;
    [ObservableProperty] private WealthSnapshot? _selectedSnapshot;
    [ObservableProperty] private bool _isShowingUnpriced;

    // ── Update ──────────────────────────────────────────────────
    private readonly UpdateService _updateService = new();

    [ObservableProperty] private bool _updateAvailable;
    [ObservableProperty] private string _updateText = "";
    [ObservableProperty] private string _currentVersion = "";
    [ObservableProperty] private bool _isUpdating;
    [ObservableProperty] private int _updateProgress;
    [ObservableProperty] private string _updateProgressText = "";

    private UpdateInfo? _pendingUpdate;


    // ── Chart ───────────────────────────────────────────────────
    [ObservableProperty] private ISeries[] _chartSeries = Array.Empty<ISeries>();
    [ObservableProperty] private Axis[] _chartXAxes = new Axis[] { new() };
    [ObservableProperty] private Axis[] _chartYAxes = new Axis[] { new() };
    private List<WealthSnapshot> _lastSnapshots = [];
    private List<TabSummary> _liveTabs = [];

    // ── Session profit ───────────────────────────────────────────
    [ObservableProperty] private string _sessionProfitText = "";
    [ObservableProperty] private bool _hasSessionProfit;
    [ObservableProperty] private bool _isSessionPositive = true;
    private double _sessionStartDivine;
    private bool _sessionStartSet;

    // ── Goal ────────────────────────────────────────────────────
    [ObservableProperty] private double _divineGoal;
    [ObservableProperty] private double _goalProgress;
    [ObservableProperty] private string _goalText = "";
    [ObservableProperty] private bool _hasGoal;

    // ── Notes ───────────────────────────────────────────────────
    [ObservableProperty] private string _editingNote = "";
    [ObservableProperty] private bool _hasSelectedSnapshot;
    private bool _loadingNote;
    private CancellationTokenSource? _noteSaveCts;

    // ── Diff ────────────────────────────────────────────────────
    [ObservableProperty] private WealthSnapshot? _pinnedSnapshot;
    [ObservableProperty] private string _pinnedSnapshotId = "";
    [ObservableProperty] private bool _isDiffMode;
    [ObservableProperty] private bool _canShowDiff;
    public ObservableCollection<DiffItem> DiffItems { get; } = [];

    // ── Modifier checker ────────────────────────────────────────
    [ObservableProperty] private bool _isShowingModItems;
    public ObservableCollection<ModCheckedItem> ModCheckedItems { get; } = [];
    public List<ModifierProfile> ModifierProfiles { get; private set; } = [];

    // ── Collections ─────────────────────────────────────────────
    public ObservableCollection<TabSummary> Tabs { get; } = [];
    public RangeObservableCollection<PricedItem> SelectedTabItems { get; } = new();
    public ObservableCollection<UnpricedItem> UnpricedItems { get; } = [];
    public ObservableCollection<WealthSnapshot> History { get; } = [];

    // ── Chart colors ────────────────────────────────────────────
    private static readonly SKColor Green = SKColor.Parse("#4ade80");
    private static readonly SKColor Red = SKColor.Parse("#ef4444");
    private static readonly SKColor Gold = SKColor.Parse("#f1c40f");
    private static readonly SKColor GridColor = SKColor.Parse("#333355");
    private static readonly SKColor LabelColor = SKColor.Parse("#888888");
    private static readonly SKColor DotBorder = new(30, 30, 46);

    public SolidColorPaint ChartTooltipBackground { get; } = new(SKColor.Parse("#27272a"));
    public SolidColorPaint ChartTooltipText { get; } = new(SKColor.Parse("#f4f4f5"));

    public MainViewModel()
    {
        _prices = new PriceService(new HttpClient());
        _stash = new StashService();
        _resolver = new PriceResolver(_prices);
        _data = new DataService();

        _autoTimer = new DispatcherTimer();
        _autoTimer.Tick += async (_, _) => await RefreshAsync();

        CurrentVersion = $"v{UpdateService.GetCurrentVersion()}";

        LoadFromSettings();

        // Check for updates in background after startup
        _ = CheckForUpdateAsync();
    }

    // ── Update commands ─────────────────────────────────────────

    private async Task CheckForUpdateAsync()
    {
        try
        {
            // Small delay so the UI loads first
            await Task.Delay(3000);

            _pendingUpdate = await _updateService.CheckForUpdateAsync();

            if (_pendingUpdate != null)
            {
                UpdateAvailable = true;
                UpdateText =
                    $"Update available: {_pendingUpdate.LatestVersion} " +
                    $"({_pendingUpdate.FileSizeDisplay})";
            }
        }
        catch
        {
            // Silent fail — update check is non-critical
        }
    }

    [RelayCommand]
    private async Task InstallUpdateAsync()
    {
        if (_pendingUpdate == null) return;

        var result = MessageBox.Show(
            $"Download and install {_pendingUpdate.LatestVersion}?\n\n" +
            $"Size: {_pendingUpdate.FileSizeDisplay}\n" +
            $"The application will restart after updating.\n\n" +
            $"Release notes:\n{TruncateNotes(_pendingUpdate.ReleaseNotes)}",
            "Update Available",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information,
            MessageBoxResult.Yes);

        if (result != MessageBoxResult.Yes) return;

        IsUpdating = true;
        UpdateProgressText = "Starting download…";

        var progress = new Progress<(int Percent, string Status)>(p =>
        {
            UpdateProgress = p.Percent;
            UpdateProgressText = p.Status;
        });

        var success = await _updateService.DownloadAndInstallAsync(
            _pendingUpdate, progress);

        if (success)
        {
            UpdateProgressText = "Restarting…";
            // Give the updater script a moment to start
            await Task.Delay(500);
            Application.Current.Shutdown();
        }
        else
        {
            IsUpdating = false;
            UpdateProgressText = "";
            MessageBox.Show(
                "Update failed. Please try again or download manually from GitHub.",
                "Update Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private void DismissUpdate()
    {
        UpdateAvailable = false;
        _pendingUpdate = null;
    }

    [RelayCommand]
    private void OpenReleasePage()
    {
        if (_pendingUpdate?.ReleaseUrl is { } url)
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }

    private static string TruncateNotes(string notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return "(no release notes)";
        return notes.Length > 500 ? notes[..500] + "…" : notes;
    }

    // ── Search ──────────────────────────────────────────────────

    private CancellationTokenSource? _searchCts;
    private const int SearchDebounceMs = 200;

    partial void OnSearchTextChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        Task.Delay(SearchDebounceMs, token).ContinueWith(_ =>
        {
            if (!token.IsCancellationRequested)
                App.Current.Dispatcher.Invoke(() => ApplyFilter());
        }, TaskScheduler.Default);
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = "";

    private void ApplyFilter()
    {
        IEnumerable<PricedItem> filtered = _allCurrentItems
            .OrderByDescending(x => x.TotalPriceChaos);

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var terms = SearchText.Trim().Split(' ',
                StringSplitOptions.RemoveEmptyEntries);

            filtered = filtered.Where(item =>
                terms.All(term =>
                    item.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    item.Category.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    item.TabName.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        var list = filtered.ToList();

        // Single UI notification instead of one per item
        SelectedTabItems.ReplaceAll(list);

        UpdateItemCount(list.Count, _allCurrentItems.Count);
    }

    /// <summary>
    /// Replaces all items in an ObservableCollection in one batch.
    /// Much faster than Clear() + individual Add() calls.
    /// </summary>
    private static void ReplaceCollection<T>(ObservableCollection<T> collection, List<T> newItems)
    {
        collection.Clear();

        // If small list, just add normally
        if (newItems.Count <= 50)
        {
            foreach (var item in newItems)
                collection.Add(item);
            return;
        }

        // For large lists, suppress notifications by using the
        // underlying list directly via reflection-free approach:
        // add all items then force a single reset notification
        foreach (var item in newItems)
            collection.Add(item);
    }

    private void UpdateItemCount(int shown, int total)
    {
        if (total == 0)
            ItemCountText = "";
        else if (shown == total)
            ItemCountText = $"{total} items";
        else
            ItemCountText = $"{shown} / {total} items";
    }

    // ── Show unpriced tab ───────────────────────────────────────

    [RelayCommand]
    private void ShowUnpriced()
    {
        _updatingSelection = true;
        SelectedTab = null;
        SelectedSnapshot = null;
        _updatingSelection = false;

        IsShowingModItems = false;
        IsShowingUnpriced = true;
        _allCurrentItems = [];
        SelectedTabItems.Clear();
        ItemsHeader = $"⚠ Unpriced Items — {UnpricedItems.Count} items not matched";
        SearchText = "";
        ItemCountText = $"{UnpricedItems.Count} items";
    }

    // ── Refresh ─────────────────────────────────────────────────

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task RefreshAsync(CancellationToken ct = default)
    {
        var settings = _data.LoadSettings();

        var sessionId = CredentialService.Load();
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            StatusText = "⚠ No POESESSID stored. Open Settings and save one.";
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.League))
        {
            StatusText = "⚠ No league set. Open Settings first.";
            return;
        }

        var syncedTabs = settings.Tabs.Where(t => t.IsSynced).ToList();
        if (syncedTabs.Count == 0)
        {
            StatusText = "⚠ No tabs selected. Open Settings and check some tabs.";
            return;
        }

        IsBusy = true;
        RefreshProgress = 0;
        RefreshTotal = 1 + syncedTabs.Count;
        var league = settings.League.Trim();

        try
        {
            // 1 ── Prices
            if (_prices.IsCacheValid(league, settings.PriceCacheMinutes))
            {
                var remaining = _prices.CacheRemainingMinutes(settings.PriceCacheMinutes);
                StatusText = $"Using cached prices ({remaining} min remaining)…";
                await Task.Delay(300, ct);
            }
            else
            {
                var progress = new Progress<string>(s => StatusText = s);
                await _prices.LoadAsync(league, progress, ct);
            }

            DivinePrice = _prices.DivinePrice;
            PriceEntries = _prices.EntryCount;
            RefreshProgress = 1;

            // 2 ── Fetch all synced tabs in parallel
            _stash.SetSessionId(sessionId);
            StatusText = $"Loading {syncedTabs.Count} tabs…";
            foreach (var tab in _liveTabs)
                tab.IsRefreshing = true;

            var tabIndices = syncedTabs.Select(t => t.Index).ToList();
            int tabsDone = 0;
            var tabProgress = new Progress<string>(s =>
            {
                StatusText = s;
                RefreshProgress = 1 + ++tabsDone;
            });

            var results = await _stash.GetMultipleTabsAsync(
                league, tabIndices, tabProgress, ct);

            // 3 ── Process results
            var tabSummaries = new List<TabSummary>();
            var allUnpriced = new List<UnpricedItem>();
            double grandTotal = 0;

            foreach (var (tabIndex, items, err) in results)
            {
                var tabInfo = syncedTabs.First(t => t.Index == tabIndex);

                if (err != null)
                {
                    StatusText = $"⚠ Tab '{tabInfo.Name}': {err}";
                    continue;
                }

                var rawItems = items ?? new List<StashItem>();
                var (priced, unpriced) = _resolver.PriceTab(
                    rawItems,
                    DivinePrice, settings.MinItemValueChaos, tabInfo.Name, tabInfo.Index);
                double tabTotal = priced.Sum(x => x.TotalPriceChaos);

                allUnpriced.AddRange(unpriced);

                var color = Color.FromRgb(
                    (byte)tabInfo.ColorR, (byte)tabInfo.ColorG, (byte)tabInfo.ColorB);

                // Mod checker: if this tab has a profile assigned, check items
                var modProfile = !string.IsNullOrEmpty(tabInfo.ModifierProfileId)
                    ? settings.ModifierProfiles.FirstOrDefault(p => p.Id == tabInfo.ModifierProfileId)
                    : null;
                var modItems = modProfile != null
                    ? PriceResolver.CheckMods(rawItems, modProfile)
                    : new List<ModCheckedItem>();

                tabSummaries.Add(new TabSummary
                {
                    Name = tabInfo.Name,
                    Index = tabInfo.Index,
                    Type = tabInfo.Type,
                    Color = color,
                    TotalChaos = Math.Round(tabTotal, 1),
                    TotalDivine = DivinePrice > 0
                        ? Math.Round(tabTotal / DivinePrice, 2) : 0,
                    ItemCount = priced.Count,
                    Items = priced,
                    ModifierProfileId   = tabInfo.ModifierProfileId,
                    ModifierProfileName = modProfile?.Name,
                    ModItems            = modItems
                });

                grandTotal += tabTotal;
            }

            // 4 ── Build "All Items" aggregate
            var allItems = tabSummaries.SelectMany(t => t.Items).ToList();
            var combined = PriceResolver.CombineDuplicates(allItems);

            var allItemsTab = new TabSummary
            {
                Name = "📦 All Items",
                Index = -1,
                Type = "Aggregate",
                Color = Color.FromRgb(241, 196, 15),
                TotalChaos = Math.Round(grandTotal, 1),
                TotalDivine = DivinePrice > 0
                    ? Math.Round(grandTotal / DivinePrice, 2) : 0,
                ItemCount = combined.Count,
                Items = combined
            };

            // 5 ── Build unpriced list
            var combinedUnpriced = PriceResolver.CombineUnpriced(allUnpriced);
            UnpricedItems.Clear();
            foreach (var item in combinedUnpriced)
                UnpricedItems.Add(item);
            UnpricedCount = combinedUnpriced.Count;
            ShowUnpricedTab = combinedUnpriced.Count > 0;

            // Sort tabs: respect saved user order, fall back to value for new tabs
            var ordered = settings.TabOrder.Count > 0
                ? tabSummaries
                    .OrderBy(t =>
                    {
                        int i = settings.TabOrder.IndexOf(t.Name);
                        return i >= 0 ? i : int.MaxValue;
                    })
                    .ThenByDescending(t => t.TotalDivine)
                    .ToList()
                : tabSummaries.OrderByDescending(t => t.TotalDivine).ToList();

            // Merge into existing Tabs list in-place so animations can play per tab
            var existingByIndex = _liveTabs.ToDictionary(t => t.Index);

            foreach (var tab in ordered)
            {
                if (existingByIndex.TryGetValue(tab.Index, out var existing))
                {
                    existing.TotalChaos          = tab.TotalChaos;
                    existing.TotalDivine         = tab.TotalDivine;
                    existing.ItemCount           = tab.ItemCount;
                    existing.Items               = tab.Items;
                    existing.ModifierProfileId   = tab.ModifierProfileId;
                    existing.ModifierProfileName = tab.ModifierProfileName;
                    existing.ModItems            = tab.ModItems;
                }
            }

            // Compute which tabs appeared or disappeared
            var newIndices      = ordered.Select(t => t.Index).ToHashSet();
            var existingIndices = _liveTabs.Select(t => t.Index).ToHashSet();

            // Update / create the "All Items" aggregate entry in _liveTabs
            var allItemsExisting = _liveTabs.FirstOrDefault(t => t.Index == -1);
            if (allItemsExisting != null)
            {
                allItemsExisting.TotalChaos  = allItemsTab.TotalChaos;
                allItemsExisting.TotalDivine = allItemsTab.TotalDivine;
                allItemsExisting.ItemCount   = allItemsTab.ItemCount;
                allItemsExisting.Items       = allItemsTab.Items;
            }

            // Rebuild _liveTabs from the merged result — never from Tabs,
            // because Tabs may currently hold snapshot data.
            _liveTabs = new List<TabSummary>
            {
                allItemsExisting ?? allItemsTab
            };
            foreach (var tab in ordered)
            {
                _liveTabs.Add(
                    existingByIndex.TryGetValue(tab.Index, out var existing)
                        ? existing
                        : tab);
            }

            // Only update the visible Tabs collection when live tabs are showing.
            // When a snapshot is selected, leave Tabs untouched; the corrected
            // _liveTabs will be applied when the snapshot is deselected.
            if (SelectedSnapshot == null)
            {
                foreach (var tab in ordered.Where(t => !existingIndices.Contains(t.Index)))
                    Tabs.Add(tab);
                foreach (var tab in _liveTabs.Where(t => t.Index >= 0 && !newIndices.Contains(t.Index)).ToList())
                    Tabs.Remove(tab);

                if (allItemsExisting == null)
                    Tabs.Insert(0, allItemsTab);
            }

            // Stagger the flash: clear IsRefreshing one tab at a time
            foreach (var tab in _liveTabs)
            {
                tab.IsRefreshing = false;
                await Task.Delay(40, ct);
            }

            // 6 ── Totals
            TotalChaos = Math.Round(grandTotal, 0);
            TotalDivine = DivinePrice > 0
                ? Math.Round(grandTotal / DivinePrice, 1) : 0;
            LastRefresh = DateTime.Now.ToString("HH:mm:ss");

            if (!_sessionStartSet) { _sessionStartDivine = TotalDivine; _sessionStartSet = true; }
            UpdateSessionProfit();
            UpdateGoal();

            // 7 ── Snapshot
            _data.AddSnapshot(new WealthSnapshot
            {
                Timestamp = DateTime.Now,
                TotalChaos = TotalChaos,
                TotalDivine = TotalDivine,
                League = league,
                Items = allItems
            });
            LoadHistory(league);

            // 8 ── Auto-select All Items (only when live tabs are visible)
            if (SelectedSnapshot == null)
                SelectedTab = _liveTabs.FirstOrDefault(t => t.Index == -1);

            var cacheNote = _prices.LastLoadedAt.HasValue
                ? $"  •  Prices from {_prices.LastLoadedAt:HH:mm}"
                : "";
            var unpricedNote = combinedUnpriced.Count > 0
                ? $"  •  {combinedUnpriced.Count} unpriced"
                : "";

            StatusText =
                $"✅ Done — {tabSummaries.Count} tabs, " +
                $"{TotalDivine:N1} div ({TotalChaos:N0}c){cacheNote}{unpricedNote}";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"❌ {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            RefreshProgress = 0;
            RefreshTotal = 1;
        }
    }



    // ── Selection ───────────────────────────────────────────────

    partial void OnSelectedTabChanged(TabSummary? value)
    {
        if (_updatingSelection) return;
        _updatingSelection = true;

        SelectedSnapshot = null;

        if (value?.IsModCheckerTab == true && value.ModItems.Count > 0)
        {
            ShowModItems(value);
        }
        else if (value?.IsModCheckerTab == true && value.ModItems.Count == 0)
        {
            // Profile assigned but tab not yet refreshed
            IsShowingModItems = false;
            ShowItems(value.Items, value.Name);
            if (value.Items.Count == 0)
                ItemsHeader = $"🔍 {value.ModifierProfileName ?? "Modifier Check"} — Refresh to scan modifiers";
        }
        else
        {
            IsShowingModItems = false;
            ShowItems(value?.Items, value?.Name ?? "(select a tab or snapshot)");
        }

        _updatingSelection = false;
    }

    private void ShowModItems(TabSummary tab)
    {
        IsShowingUnpriced  = false;
        IsShowingModItems  = true;
        _allCurrentItems   = [];
        SearchText         = "";

        ModCheckedItems.Clear();
        foreach (var item in tab.ModItems)
            ModCheckedItems.Add(item);

        int matchCount = tab.ModItems.Count(x => x.IsMatch);
        ItemsHeader  = $"🔍 {tab.ModifierProfileName ?? "Modifier Check"} — {matchCount} match{(matchCount == 1 ? "" : "es")} / {tab.ModItems.Count} items";
        ItemCountText = $"{matchCount} match{(matchCount == 1 ? "" : "es")}";
    }

    partial void OnSelectedSnapshotChanged(WealthSnapshot? value)
    {
        if (_updatingSelection) return;
        _updatingSelection = true;

        SelectedTab = null;

        _loadingNote = true;
        EditingNote = value?.Note ?? "";
        _loadingNote = false;
        HasSelectedSnapshot = value != null;
        IsDiffMode = false;
        CanShowDiff = value != null && PinnedSnapshot != null && PinnedSnapshot.Id != value.Id;

        if (value != null)
        {
            BuildSnapshotTabs(value);
            var combined = PriceResolver.CombineDuplicates(value.Items);
            var header = value.HasPrevious
                ? $"Snapshot — {value.Timestamp:g}  {value.PercentChangeText}"
                : $"Snapshot — {value.Timestamp:g}";
            ShowItems(combined, header);
        }
        else
        {
            RestoreLiveTabs();
            ShowItems(null, "(select a tab or snapshot)");
        }

        _updatingSelection = false;
    }

    private void BuildSnapshotTabs(WealthSnapshot snapshot)
    {
        foreach (var item in snapshot.Items)
        {
            var (sparkData, sparkTrend) = _prices.GetSparkline(item.Name);
            item.SparklineData = sparkData;
            item.SparklineTrend = sparkTrend;
        }

        var settings = _data.LoadSettings();
        var colorMap = settings.Tabs.ToDictionary(
            t => t.Index,
            t => Color.FromRgb((byte)t.ColorR, (byte)t.ColorG, (byte)t.ColorB));

        var unsortedTabs = snapshot.Items
            .GroupBy(i => i.TabIndex)
            .Select(g =>
            {
                var items = g.ToList();
                double totalChaos = items.Sum(i => i.TotalPriceChaos);
                double divinePrice = items.FirstOrDefault()?.DivinePrice ?? 1;
                colorMap.TryGetValue(g.Key, out var color);
                return new TabSummary
                {
                    Name = items[0].TabName,
                    Index = g.Key,
                    Color = color.A == 0 ? Color.FromRgb(99, 128, 0) : color,
                    Items = items,
                    TotalChaos = Math.Round(totalChaos, 1),
                    TotalDivine = divinePrice > 0 ? Math.Round(totalChaos / divinePrice, 2) : 0,
                    ItemCount = items.Count
                };
            })
            .ToList();

        var snapshotTabs = settings.TabOrder.Count > 0
            ? unsortedTabs
                .OrderBy(t =>
                {
                    int i = settings.TabOrder.IndexOf(t.Name);
                    return i >= 0 ? i : int.MaxValue;
                })
                .ThenByDescending(t => t.TotalDivine)
                .ToList()
            : unsortedTabs.OrderByDescending(t => t.TotalDivine).ToList();

        double grandTotal = snapshot.Items.Sum(i => i.TotalPriceChaos);
        double dp = snapshot.Items.FirstOrDefault()?.DivinePrice ?? 1;

        Tabs.Clear();
        Tabs.Add(new TabSummary
        {
            Name = "📦 All Items",
            Index = -1,
            Color = Color.FromRgb(241, 196, 15),
            Items = PriceResolver.CombineDuplicates(snapshot.Items),
            TotalChaos = Math.Round(grandTotal, 1),
            TotalDivine = dp > 0 ? Math.Round(grandTotal / dp, 2) : 0,
            ItemCount = snapshot.Items.Count
        });
        foreach (var tab in snapshotTabs)
            Tabs.Add(tab);
    }

    private void RestoreLiveTabs()
    {
        Tabs.Clear();
        foreach (var tab in _liveTabs)
            Tabs.Add(tab);
    }

    private void ShowItems(List<PricedItem>? items, string header)
    {
        IsShowingUnpriced = false;
        IsShowingModItems = false;
        _allCurrentItems = items ?? [];
        ItemsHeader = header;
        SearchText = "";
        ApplyFilter();
    }



    // ── Session profit ───────────────────────────────────────────

    [RelayCommand]
    private void ResetSession()
    {
        _sessionStartDivine = TotalDivine;
        UpdateSessionProfit();
    }

    private void UpdateSessionProfit()
    {
        if (!_sessionStartSet || TotalDivine <= 0) return;
        double profit = TotalDivine - _sessionStartDivine;
        HasSessionProfit = true;
        IsSessionPositive = profit >= 0;
        SessionProfitText = profit >= 0
            ? $"+{profit:N1} div this session"
            : $"{profit:N1} div this session";
    }

    // ── Goal ────────────────────────────────────────────────────

    private void UpdateGoal()
    {
        var settings = _data.LoadSettings();
        DivineGoal = settings.DivineGoal;
        HasGoal = DivineGoal > 0;
        if (HasGoal && TotalDivine > 0)
        {
            GoalProgress = Math.Min(100, (TotalDivine / DivineGoal) * 100);
            GoalText = $"{TotalDivine:N1} / {DivineGoal:N0} div  ({GoalProgress:N0}%)";
        }
    }

    // ── Notes ───────────────────────────────────────────────────

    partial void OnEditingNoteChanged(string value)
    {
        if (_loadingNote || SelectedSnapshot == null) return;
        SelectedSnapshot.Note = value;

        _noteSaveCts?.Cancel();
        _noteSaveCts = new CancellationTokenSource();
        var id = SelectedSnapshot.Id;
        var token = _noteSaveCts.Token;
        Task.Delay(800, token).ContinueWith(_ =>
        {
            if (!token.IsCancellationRequested)
                _data.UpdateSnapshotNote(id, value);
        }, TaskScheduler.Default);
    }

    // ── Diff ────────────────────────────────────────────────────

    [RelayCommand]
    private void PinSnapshot(WealthSnapshot snapshot)
    {
        PinnedSnapshot = snapshot;
        PinnedSnapshotId = PinnedSnapshot?.Id ?? "";
        CanShowDiff = PinnedSnapshot != null && SelectedSnapshot != null
                      && PinnedSnapshot.Id != SelectedSnapshot.Id;
        IsDiffMode = false;
    }

    [RelayCommand]
    private void ClearPin()
    {
        PinnedSnapshot = null;
        PinnedSnapshotId = "";
        CanShowDiff = false;
        IsDiffMode = false;
    }

    [RelayCommand]
    private void ToggleDiff()
    {
        if (!CanShowDiff) return;
        IsDiffMode = !IsDiffMode;
        if (IsDiffMode) RefreshDiff();
    }

    private void RefreshDiff()
    {
        if (PinnedSnapshot == null || SelectedSnapshot == null) return;

        // from = older, to = newer
        var from = PinnedSnapshot.Timestamp < SelectedSnapshot.Timestamp
            ? PinnedSnapshot : SelectedSnapshot;
        var to = PinnedSnapshot.Timestamp < SelectedSnapshot.Timestamp
            ? SelectedSnapshot : PinnedSnapshot;

        var fromMap = PriceResolver.CombineDuplicates(from.Items)
            .ToDictionary(i => i.Name, StringComparer.OrdinalIgnoreCase);
        var toMap = PriceResolver.CombineDuplicates(to.Items)
            .ToDictionary(i => i.Name, StringComparer.OrdinalIgnoreCase);

        var allNames = fromMap.Keys.Union(toMap.Keys, StringComparer.OrdinalIgnoreCase);
        var results = new List<DiffItem>();

        foreach (var name in allNames)
        {
            fromMap.TryGetValue(name, out var f);
            toMap.TryGetValue(name, out var t);

            int oldQty = f?.Quantity ?? 0;
            int newQty = t?.Quantity ?? 0;
            double oldChaos = f?.TotalPriceChaos ?? 0;
            double newChaos = t?.TotalPriceChaos ?? 0;
            double oldDiv = f?.TotalPriceDivine ?? 0;
            double newDiv = t?.TotalPriceDivine ?? 0;

            if (oldQty == newQty && Math.Abs(oldChaos - newChaos) < 0.5) continue;

            results.Add(new DiffItem
            {
                Name = name,
                Icon = (t ?? f)?.Icon,
                OldQty = oldQty,
                NewQty = newQty,
                OldValueChaos = oldChaos,
                NewValueChaos = newChaos,
                OldValueDivine = oldDiv,
                NewValueDivine = newDiv,
            });
        }

        DiffItems.Clear();
        foreach (var d in results.OrderByDescending(d => Math.Abs(d.ValueChangeChaos)))
            DiffItems.Add(d);
    }

    // ── Delete snapshots ────────────────────────────────────────

    [RelayCommand]
    private void DeleteSnapshot(string id)
    {
        if (string.IsNullOrEmpty(id)) return;

        if (SelectedSnapshot?.Id == id)
        {
            SelectedSnapshot = null;
            ShowItems(null, "(select a tab or snapshot)");
        }

        _data.DeleteSnapshot(id);
        var settings = _data.LoadSettings();
        LoadHistory(settings.League);
    }

    [RelayCommand]
    private void DeleteAllSnapshots()
    {
        var settings = _data.LoadSettings();
        if (string.IsNullOrWhiteSpace(settings.League)) return;

        var league = settings.League.Trim();
        var result = MessageBox.Show(
            $"Delete all wealth snapshots for \"{league}\"?\n\nThis cannot be undone.",
            "Delete All Snapshots",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result != MessageBoxResult.Yes) return;

        _data.DeleteAllSnapshots(league);
        SelectedSnapshot = null;
        ShowItems(null, "(select a tab or snapshot)");
        History.Clear();
        BuildChart(new List<WealthSnapshot>());
    }

    // ── Settings ────────────────────────────────────────────────

    [RelayCommand]
    private void OpenSettings()
    {
        var window = new SettingsWindow
        {
            Owner = App.Current.MainWindow
        };
        window.ShowDialog();
        LoadFromSettings();
    }

    // ── Modifier profile assignment ──────────────────────────────

    /// <summary>
    /// Assigns (or removes) a modifier profile from a tab and persists to settings.
    /// Called from the code-behind right-click context menu.
    /// </summary>
    public void SetTabModifierProfile(TabSummary tab, string? profileId)
    {
        var s = _data.LoadSettings();
        var savedTab = s.Tabs.FirstOrDefault(t => t.Index == tab.Index);
        if (savedTab == null) return;

        savedTab.ModifierProfileId = profileId;
        _data.SaveSettings(s);

        // Update in-memory tab
        var profile = profileId != null
            ? s.ModifierProfiles.FirstOrDefault(p => p.Id == profileId)
            : null;
        tab.ModifierProfileId   = profileId;
        tab.ModifierProfileName = profile?.Name;

        // If mod items haven't been loaded yet (no refresh), clear them
        if (profileId == null)
            tab.ModItems = [];

        // If this tab is currently selected, refresh the view
        if (SelectedTab == tab)
        {
            if (tab.IsModCheckerTab && tab.ModItems.Count > 0)
                ShowModItems(tab);
            else
            {
                IsShowingModItems = false;
                ShowItems(tab.Items, tab.Name);
                if (tab.IsModCheckerTab && tab.Items.Count == 0)
                    ItemsHeader = $"🔍 {tab.ModifierProfileName ?? "Modifier Check"} — Refresh to scan modifiers";
            }
        }
    }

    // ── Chart ───────────────────────────────────────────────────

    private void BuildChart(List<WealthSnapshot> newestFirst)
    {
        var ordered = newestFirst.OrderBy(s => s.Timestamp).ToList();
        int n = ordered.Count;

        if (n == 0)
        {
            ChartSeries = Array.Empty<ISeries>();
            ChartXAxes = new Axis[] { new() };
            ChartYAxes = new Axis[] { new() };
            return;
        }

        var series = new List<ISeries>();

        if (n == 1)
        {
            series.Add(new LineSeries<ObservablePoint>
            {
                Values = new ObservablePoint[] { new(0, ordered[0].TotalDivine) },
                Stroke = null,
                Fill = null,
                GeometrySize = 10,
                GeometryFill = new SolidColorPaint(Gold),
                GeometryStroke = new SolidColorPaint(DotBorder, 2),
                IsVisibleAtLegend = false,
            });
        }
        else
        {
            for (int i = 1; i < n; i++)
            {
                var prev = ordered[i - 1].TotalDivine;
                var curr = ordered[i].TotalDivine;
                var color = curr >= prev ? Green : Red;

                series.Add(new LineSeries<ObservablePoint>
                {
                    Values = new ObservablePoint[] { new(i - 1, prev), new(i, curr) },
                    Stroke = new SolidColorPaint(color, 2.5f),
                    Fill = null,
                    GeometrySize = 7,
                    GeometryFill = new SolidColorPaint(color),
                    GeometryStroke = new SolidColorPaint(DotBorder, 1.5f),
                    LineSmoothness = 0,
                    IsVisibleAtLegend = false,
                    IsHoverable = false,
                });
            }
        }

        ChartSeries = series.ToArray();
        string[] xLabels = ordered.Select(s => s.Timestamp.ToString("dd.MM HH:mm")).ToArray();
        double yMin = ordered.Min(s => s.TotalDivine);
        double yMax = ordered.Max(s => s.TotalDivine);

        double spread = yMax - yMin;
        // Tight padding so variation is clearly visible (5% of spread, or 2% of value for flat lines)
        double yPad = spread > 0 ? spread * 0.05 : Math.Max(yMax * 0.02, 0.5);

        // Always show at least 5 x-slots so a small number of points don't stretch across the full width
        double xMax = Math.Max(n - 0.5, 4.5);

        ChartXAxes = new Axis[]
        {
            new()
            {
                Labels = xLabels,
                LabelsPaint = new SolidColorPaint(LabelColor),
                TextSize = 9,
                SeparatorsPaint = new SolidColorPaint(GridColor),
                ShowSeparatorLines = false,
                LabelsRotation = 0,
                MinLimit = -0.5,
                MaxLimit = xMax,
            }
        };

        ChartYAxes = new Axis[]
        {
            new()
            {
                LabelsPaint = new SolidColorPaint(LabelColor),
                TextSize = 10,
                Labeler = v => $"{v:N1}d",
                SeparatorsPaint = new SolidColorPaint(GridColor, 1),
                MinLimit = Math.Max(0, yMin - yPad),
                MaxLimit = yMax + yPad,
            }
        };
    }

    // ── Helpers ─────────────────────────────────────────────────

    public void LoadFromSettings()
    {
        var s = _data.LoadSettings();
        LeagueDisplay = string.IsNullOrWhiteSpace(s.League) ? "(not set)" : s.League;
        SyncedTabCount = s.Tabs.Count(t => t.IsSynced);
        ModifierProfiles = s.ModifierProfiles;
        LoadHistory(s.League);
        UpdateAutoTimer(s.AutoRefreshMinutes);
        UpdateGoal();
    }

    private void LoadHistory(string league)
    {
        History.Clear();
        if (string.IsNullOrWhiteSpace(league))
        {
            BuildChart(new List<WealthSnapshot>());
            return;
        }

        var snapshots = _data.LoadSnapshots(league.Trim()).Take(50).ToList();

        for (int i = 0; i < snapshots.Count; i++)
        {
            if (i < snapshots.Count - 1)
            {
                var curr = snapshots[i];
                var prev = snapshots[i + 1];
                curr.HasPrevious = true;

                if (prev.TotalDivine > 0)
                    curr.PercentChange =
                        ((curr.TotalDivine - prev.TotalDivine) / prev.TotalDivine) * 100;
            }
        }

        foreach (var snap in snapshots)
            History.Add(snap);

        _lastSnapshots = snapshots;
        BuildChart(snapshots);
    }

    private void UpdateAutoTimer(int minutes)
    {
        _autoTimer.Stop();
        if (minutes > 0)
        {
            _autoTimer.Interval = TimeSpan.FromMinutes(minutes);
            _autoTimer.Start();
        }
    }

    // ── Tab ordering ─────────────────────────────────────────────

    public void MoveTab(int fromIndex, int toIndex)
    {
        if (fromIndex == toIndex) return;
        if (fromIndex < 0 || fromIndex >= Tabs.Count) return;
        if (toIndex < 0 || toIndex >= Tabs.Count) return;

        var tab = Tabs[fromIndex];
        Tabs.RemoveAt(fromIndex);
        Tabs.Insert(toIndex, tab);

        SaveTabOrder();
    }

    private void SaveTabOrder()
    {
        var s = _data.LoadSettings();
        // Save order of real tabs only (skip the "All Items" aggregate at index 0)
        s.TabOrder = Tabs.Where(t => t.Index != -1).Select(t => t.Name).ToList();
        _data.SaveSettings(s);
    }
}