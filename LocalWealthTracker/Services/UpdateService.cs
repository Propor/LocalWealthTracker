using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using LocalWealthTracker.Models;

namespace LocalWealthTracker.Services;

/// <summary>
/// Checks GitHub Releases for updates and handles the download/install process.
/// 
/// Flow:
///   1. Check GitHub API for latest release
///   2. Compare tag version with current assembly version
///   3. If newer → show notification to user
///   4. User clicks Update → download zip → extract → run updater script → exit
///   5. Updater script waits for app to close → copies new files → restarts app
/// </summary>
public sealed class UpdateService
{
    // ⚠️ CHANGE THESE to your GitHub repo
    private const string GitHubOwner = "Propor";
    private const string GitHubRepo = "LocalWealthTracker";

    private static readonly string ApiUrl =
        $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";

    private readonly HttpClient _http;

    public UpdateService()
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "LocalWealthTracker-UpdateChecker/1.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        _http.Timeout = TimeSpan.FromSeconds(15);
    }

    /// <summary>
    /// Gets the current application version from the assembly.
    /// </summary>
    public static string GetCurrentVersion()
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        // Strip build metadata (e.g. "1.0.0+abc123" → "1.0.0")
        if (version != null && version.Contains('+'))
            version = version[..version.IndexOf('+')];

        return version ?? "0.0.0";
    }

    /// <summary>
    /// Checks GitHub for the latest release.
    /// Returns null if no update is available or if the check fails.
    /// </summary>
    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await _http.GetStringAsync(ApiUrl, ct);
            var release = JsonSerializer.Deserialize<GitHubRelease>(json);

            if (release == null) return null;

            // Find the zip asset
            var asset = release.Assets.FirstOrDefault(a =>
                a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

            if (asset == null) return null;

            var currentVersion = GetCurrentVersion();

            var info = new UpdateInfo
            {
                CurrentVersion = currentVersion,
                LatestVersion = release.TagName,
                DownloadUrl = asset.DownloadUrl,
                ReleaseUrl = release.HtmlUrl,
                ReleaseNotes = release.Body,
                PublishedAt = release.PublishedAt,
                FileSizeBytes = asset.Size
            };

            return info.IsUpdateAvailable ? info : null;
        }
        catch
        {
            // Silently fail — update check is non-critical
            return null;
        }
    }

    /// <summary>
    /// Downloads the update zip, extracts it, creates an updater script,
    /// and launches it. The caller should exit the application after this returns.
    /// </summary>
    public async Task<bool> DownloadAndInstallAsync(
        UpdateInfo update,
        IProgress<(int Percent, string Status)>? progress = null,
        CancellationToken ct = default)
    {
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var tempDir = Path.Combine(Path.GetTempPath(), "LocalWealthTracker_Update");
        var zipPath = Path.Combine(tempDir, "update.zip");
        var extractDir = Path.Combine(tempDir, "extracted");

        try
        {
            // Clean up any previous update attempt
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);

            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(extractDir);

            // 1 ── Download the zip with progress
            progress?.Report((0, "Downloading update…"));

            using var response = await _http.GetAsync(
                update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? update.FileSizeBytes;

            await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            await using var fileStream = new FileStream(
                zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            long bytesRead = 0;
            int read;

            while ((read = await contentStream.ReadAsync(buffer, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                bytesRead += read;

                if (totalBytes > 0)
                {
                    int percent = (int)(bytesRead * 100 / totalBytes);
                    progress?.Report((percent,
                        $"Downloading… {bytesRead / 1_048_576.0:N1} / {totalBytes / 1_048_576.0:N1} MB"));
                }
            }

            // 2 ── Extract
            progress?.Report((100, "Extracting…"));
            ZipFile.ExtractToDirectory(zipPath, extractDir, true);

            // 3 ── Create updater batch script
            progress?.Report((100, "Preparing update…"));
            var scriptPath = CreateUpdaterScript(appDir, extractDir, tempDir);

            // 4 ── Launch updater and signal caller to exit
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{scriptPath}\"",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = false
            });

            return true;
        }
        catch
        {
            // Clean up on failure
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
            catch { }

            return false;
        }
    }

    /// <summary>
    /// Creates a batch script that waits for the app to close,
    /// copies new files, and restarts the app.
    /// </summary>
    private static string CreateUpdaterScript(
        string appDir, string extractDir, string tempDir)
    {
        var exeName = Path.GetFileName(Environment.ProcessPath)
            ?? "LocalWealthTracker.exe";
        var exePath = Path.Combine(appDir, exeName);
        var scriptPath = Path.Combine(tempDir, "update.bat");
        var pid = Environment.ProcessId;

        var script = $"""
            @echo off
            echo Waiting for application to close...
            
            :wait
            tasklist /fi "PID eq {pid}" 2>nul | find "{pid}" >nul
            if not errorlevel 1 (
                timeout /t 1 /nobreak >nul
                goto wait
            )
            
            echo Copying new files...
            xcopy /s /y /q "{extractDir}\*" "{appDir}"
            
            echo Starting updated application...
            start "" "{exePath}"
            
            echo Cleaning up...
            rmdir /s /q "{tempDir}"
            
            exit
            """;

        File.WriteAllText(scriptPath, script);
        return scriptPath;
    }
}