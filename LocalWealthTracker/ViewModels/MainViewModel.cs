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

            // 2 ── Fetch all synced tabs in parallel
            _stash.SetSessionId(sessionId);
            StatusText = $"Loading {syncedTabs.Count} tabs…";

            var tabIndices = syncedTabs.Select(t => t.Index).ToList();
            var tabProgress = new Progress<string>(s => StatusText = s);

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

                var (priced, unpriced) = _resolver.PriceTab(
                    items ?? new List<StashItem>(),
                    DivinePrice, settings.MinItemValueChaos, tabInfo.Name, tabInfo.Index);
                double tabTotal = priced.Sum(x => x.TotalPriceChaos);

                allUnpriced.AddRange(unpriced);

                var color = Color.FromRgb(
                    (byte)tabInfo.ColorR, (byte)tabInfo.ColorG, (byte)tabInfo.ColorB);

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
                    Items = priced
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

            Tabs.Clear();
            Tabs.Add(allItemsTab);
            foreach (var tab in ordered)
                Tabs.Add(tab);
            _liveTabs = Tabs.ToList();

            // 6 ── Totals
            TotalChaos = Math.Round(grandTotal, 0);
            TotalDivine = DivinePrice > 0
                ? Math.Round(grandTotal / DivinePrice, 1) : 0;
            LastRefresh = DateTime.Now.ToString("HH:mm:ss");

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

            // 8 ── Auto-select All Items
            SelectedTab = allItemsTab;

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
        }
    }



    // ── Selection ───────────────────────────────────────────────

    partial void OnSelectedTabChanged(TabSummary? value)
    {
        if (_updatingSelection) return;
        _updatingSelection = true;

        SelectedSnapshot = null;
        ShowItems(value?.Items, value?.Name ?? "(select a tab or snapshot)");

        _updatingSelection = false;
    }

    partial void OnSelectedSnapshotChanged(WealthSnapshot? value)
    {
        if (_updatingSelection) return;
        _updatingSelection = true;

        SelectedTab = null;

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
        var settings = _data.LoadSettings();
        var colorMap = settings.Tabs.ToDictionary(
            t => t.Index,
            t => Color.FromRgb((byte)t.ColorR, (byte)t.ColorG, (byte)t.ColorB));

        var snapshotTabs = snapshot.Items
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
            .OrderByDescending(t => t.TotalDivine)
            .ToList();

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
        _allCurrentItems = items ?? [];
        ItemsHeader = header;
        SearchText = "";
        ApplyFilter();
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
        double pad = spread > 0 ? spread * 0.25 : Math.Max(yMax * 0.05, 1.0);

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
                MinLimit = yMin - pad,
                MaxLimit = yMax + pad,
            }
        };
    }

    // ── Helpers ─────────────────────────────────────────────────

    public void LoadFromSettings()
    {
        var s = _data.LoadSettings();
        LeagueDisplay = string.IsNullOrWhiteSpace(s.League) ? "(not set)" : s.League;
        SyncedTabCount = s.Tabs.Count(t => t.IsSynced);
        LoadHistory(s.League);
        UpdateAutoTimer(s.AutoRefreshMinutes);
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