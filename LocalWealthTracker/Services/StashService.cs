using System.Net;
using System.Net.Http;
using System.Text.Json;
using LocalWealthTracker.Models;

namespace LocalWealthTracker.Services;

/// <summary>
/// Reads stash tabs from the PoE website API.
/// POESESSID is ONLY sent to pathofexile.com.
/// 
/// Rate limits (from API headers):
///   ~30 requests per 60 seconds for stash tab endpoint.
///   We use a semaphore to limit concurrency and read
///   the rate limit headers to adapt.
/// </summary>
public sealed class StashService : IDisposable
{
    private const string BaseUrl =
        "https://www.pathofexile.com/character-window/get-stash-items";

    /// <summary>Max concurrent requests to the stash API.</summary>
    private const int MaxConcurrency = 5;

    /// <summary>
    /// Small delay between launching requests to avoid burst.
    /// 200ms × 5 tabs = 1s total stagger, then they complete in parallel.
    /// </summary>
    private const int StaggerDelayMs = 200;

    private HttpClient? _http;
    private readonly SemaphoreSlim _semaphore = new(MaxConcurrency, MaxConcurrency);

    public void SetSessionId(string sessionId)
    {
        _http?.Dispose();

        var cookies = new CookieContainer();
        cookies.Add(
            new Uri("https://www.pathofexile.com"),
            new Cookie("POESESSID", sessionId));

        var handler = new HttpClientHandler
        {
            CookieContainer = cookies,
            UseCookies = true,
            AllowAutoRedirect = false
        };

        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "LocalWealthTracker/1.0 (local-tool)");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    /// <summary>Fetches the list of all stash tabs (no items).</summary>
    public async Task<(List<StashTab>? Tabs, string? Error)>
        GetTabListAsync(string league, CancellationToken ct = default)
    {
        EnsureClient();
        var url = $"{BaseUrl}?league={Uri.EscapeDataString(league)}" +
                  "&tabs=1&tabIndex=0";

        var (response, error) = await SafeRequestAsync(url, ct);
        if (error != null) return (null, error);

        var data = JsonSerializer.Deserialize<StashTabResponse>(response!);
        if (data?.Error != null)
            return (null, $"PoE API: {data.Error.Message}");

        return (data?.Tabs, null);
    }

    /// <summary>Fetches all items in a single stash tab.</summary>
    public async Task<(List<StashItem>? Items, string? Error)>
        GetTabItemsAsync(string league, int tabIndex,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
    {
        EnsureClient();

        await _semaphore.WaitAsync(ct);
        try
        {
            var url = $"{BaseUrl}?league={Uri.EscapeDataString(league)}" +
                      $"&tabs=0&tabIndex={tabIndex}";

            var (response, error) = await SafeRequestAsync(url, ct, progress: progress);
            if (error != null) return (null, error);

            var data = JsonSerializer.Deserialize<StashTabResponse>(response!);
            if (data?.Error != null)
                return (null, data.Error.Message);

            return (data?.Items ?? new List<StashItem>(), null);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Fetches multiple tabs in parallel with controlled concurrency.
    /// Returns results in the same order as the input indices.
    /// </summary>
    public async Task<List<(int TabIndex, List<StashItem>? Items, string? Error)>>
        GetMultipleTabsAsync(string league, IReadOnlyList<int> tabIndices,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
    {
        EnsureClient();

        var results = new (int TabIndex, List<StashItem>? Items, string? Error)[tabIndices.Count];
        int completed = 0;

        var tasks = new List<Task>();

        for (int i = 0; i < tabIndices.Count; i++)
        {
            var index = i; // capture for closure
            var tabIndex = tabIndices[i];

            var task = Task.Run(async () =>
            {
                var (items, error) = await GetTabItemsAsync(league, tabIndex, progress, ct);
                results[index] = (tabIndex, items, error);

                var done = Interlocked.Increment(ref completed);
                progress?.Report($"Loaded tab {done}/{tabIndices.Count}…");
            }, ct);

            tasks.Add(task);

            // Stagger launches to avoid burst
            if (i < tabIndices.Count - 1)
                await Task.Delay(StaggerDelayMs, ct);
        }

        await Task.WhenAll(tasks);

        return results.ToList();
    }

    public void Dispose()
    {
        _http?.Dispose();
        _semaphore.Dispose();
    }

    // ── Helpers ─────────────────────────────────────────────────

    private void EnsureClient()
    {
        if (_http == null)
            throw new InvalidOperationException(
                "Call SetSessionId() before making requests.");
    }

    /// <summary>
    /// Makes a request with automatic retry on 429 (rate limit).
    /// Reads rate limit headers to determine wait time.
    /// Reports a per-second countdown via <paramref name="progress"/> while waiting.
    /// </summary>
    private async Task<(string? Body, string? Error)>
        SafeRequestAsync(string url, CancellationToken ct, int retries = 2,
            IProgress<string>? progress = null)
    {
        for (int attempt = 0; attempt <= retries; attempt++)
        {
            try
            {
                var response = await _http!.GetAsync(url, ct);

                // Auth failure
                if (response.StatusCode is HttpStatusCode.Found
                    or HttpStatusCode.Forbidden)
                    return (null, "Authentication failed – check your POESESSID.");

                // Rate limited — read Retry-After or wait default
                if (response.StatusCode == (HttpStatusCode)429)
                {
                    var waitSeconds = GetRetryAfterSeconds(response);
                    if (attempt < retries)
                    {
                        for (int s = waitSeconds; s > 0; s--)
                        {
                            progress?.Report($"⏳ Rate limited — retrying in {s}s…");
                            await Task.Delay(1000, ct);
                        }
                        continue;
                    }
                    return (null, $"Rate limited after {retries + 1} attempts.");
                }

                response.EnsureSuccessStatusCode();
                var body = await response.Content.ReadAsStringAsync(ct);
                return (body, null);
            }
            catch (TaskCanceledException) { throw; }
            catch (Exception ex) when (attempt < retries)
            {
                await Task.Delay(2000, ct);
                continue;
            }
            catch (Exception ex)
            {
                return (null, ex.Message);
            }
        }

        return (null, "Request failed after retries.");
    }

    /// <summary>
    /// Reads Retry-After header or falls back to a safe default.
    /// </summary>
    private static int GetRetryAfterSeconds(HttpResponseMessage response)
    {
        // Try Retry-After header
        if (response.Headers.RetryAfter?.Delta is TimeSpan delta)
            return Math.Max((int)delta.TotalSeconds, 5);

        // Try X-Rate-Limit headers for penalty info
        // Format: "X-Rate-Limit-{rule}-State: hits:period:penalty_time"
        foreach (var header in response.Headers)
        {
            if (header.Key.Contains("State", StringComparison.OrdinalIgnoreCase))
            {
                var parts = header.Value.FirstOrDefault()?.Split(':');
                if (parts?.Length >= 3 && int.TryParse(parts[2], out int penalty) && penalty > 0)
                    return penalty;
            }
        }

        // Default: 60 seconds
        return 60;
    }
}