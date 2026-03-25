using System.IO;
using System.Text.Json;
using LocalWealthTracker.Models;

namespace LocalWealthTracker.Services;

public sealed class DataService
{
    private static readonly string AppDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LocalWealthTracker");

    private static readonly string SettingsPath = Path.Combine(AppDir, "settings.json");
    private static readonly string SnapshotsPath = Path.Combine(AppDir, "snapshots.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private const int MaxSnapshots = 100;

    public DataService() => Directory.CreateDirectory(AppDir);

    // ── Settings ────────────────────────────────────────────────

    public AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(
                    File.ReadAllText(SettingsPath)) ?? new();
        }
        catch { }
        return new();
    }

    public void SaveSettings(AppSettings s) =>
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(s, JsonOpts));

    // ── Snapshots ───────────────────────────────────────────────

    /// <summary>
    /// Loads snapshots for a specific league, ordered newest first.
    /// Migrates old snapshots without IDs on first load.
    /// </summary>
    public List<WealthSnapshot> LoadSnapshots(string league)
    {
        var all = LoadAllInternal();

        // Migrate: assign IDs to old snapshots that don't have one
        bool needsSave = false;
        foreach (var snap in all)
        {
            if (string.IsNullOrEmpty(snap.Id))
            {
                snap.Id = Guid.NewGuid().ToString();
                needsSave = true;
            }
        }
        if (needsSave) SaveAllInternal(all);

        return all
            .Where(s => s.League.Equals(league, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.Timestamp)
            .ToList();
    }

    /// <summary>
    /// Adds a new snapshot. Assigns an ID if not set.
    /// Keeps at most MaxSnapshots entries.
    /// </summary>
    public void AddSnapshot(WealthSnapshot snapshot)
    {
        if (string.IsNullOrEmpty(snapshot.Id))
            snapshot.Id = Guid.NewGuid().ToString();

        var all = LoadAllInternal();
        all.Add(snapshot);

        if (all.Count > MaxSnapshots)
            all = all
                .OrderByDescending(s => s.Timestamp)
                .Take(MaxSnapshots)
                .ToList();

        SaveAllInternal(all);
    }

    /// <summary>Updates the note on a single snapshot.</summary>
    public void UpdateSnapshotNote(string id, string note)
    {
        var all = LoadAllInternal();
        var snap = all.FirstOrDefault(s => s.Id == id);
        if (snap == null) return;
        snap.Note = note;
        SaveAllInternal(all);
    }

    /// <summary>Deletes a single snapshot by ID.</summary>
    public void DeleteSnapshot(string id)
    {
        var all = LoadAllInternal();
        all.RemoveAll(s => s.Id == id);
        SaveAllInternal(all);
    }

    /// <summary>Deletes all snapshots for a given league.</summary>
    public void DeleteAllSnapshots(string league)
    {
        var all = LoadAllInternal();
        all.RemoveAll(s =>
            s.League.Equals(league, StringComparison.OrdinalIgnoreCase));
        SaveAllInternal(all);
    }

    // ── Internal helpers ────────────────────────────────────────

    private List<WealthSnapshot> LoadAllInternal()
    {
        try
        {
            if (File.Exists(SnapshotsPath))
            {
                var json = File.ReadAllText(SnapshotsPath);
                return JsonSerializer.Deserialize<List<WealthSnapshot>>(json) ?? [];
            }
        }
        catch { }
        return [];
    }

    private void SaveAllInternal(List<WealthSnapshot> all)
    {
        var json = JsonSerializer.Serialize(all, JsonOpts);
        File.WriteAllText(SnapshotsPath, json);
    }
}