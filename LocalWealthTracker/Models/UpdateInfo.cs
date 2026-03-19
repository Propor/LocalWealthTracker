using System.Text.Json.Serialization;

namespace LocalWealthTracker.Models;

/// <summary>
/// GitHub Release API response (trimmed to fields we need).
/// Endpoint: https://api.github.com/repos/{owner}/{repo}/releases/latest
/// </summary>
public sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("body")]
    public string Body { get; set; } = "";

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = "";

    [JsonPropertyName("published_at")]
    public DateTime PublishedAt { get; set; }

    [JsonPropertyName("assets")]
    public List<GitHubAsset> Assets { get; set; } = [];
}

public sealed class GitHubAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("browser_download_url")]
    public string DownloadUrl { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

/// <summary>
/// Parsed update information for display.
/// </summary>
public sealed class UpdateInfo
{
    public required string CurrentVersion { get; init; }
    public required string LatestVersion { get; init; }
    public required string DownloadUrl { get; init; }
    public required string ReleaseUrl { get; init; }
    public required string ReleaseNotes { get; init; }
    public required DateTime PublishedAt { get; init; }
    public required long FileSizeBytes { get; init; }

    public bool IsUpdateAvailable =>
        CompareVersions(LatestVersion, CurrentVersion) > 0;

    public string FileSizeDisplay =>
        FileSizeBytes > 1_048_576
            ? $"{FileSizeBytes / 1_048_576.0:N1} MB"
            : $"{FileSizeBytes / 1024.0:N0} KB";

    /// <summary>
    /// Compares two version strings (e.g. "1.2.0" vs "1.1.0").
    /// Returns positive if a > b, negative if a < b, 0 if equal.
    /// </summary>
    private static int CompareVersions(string a, string b)
    {
        // Strip leading 'v' if present
        a = a.TrimStart('v', 'V');
        b = b.TrimStart('v', 'V');

        if (Version.TryParse(a, out var va) && Version.TryParse(b, out var vb))
            return va.CompareTo(vb);

        return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }
}