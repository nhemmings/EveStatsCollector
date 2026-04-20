using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace EveStatsCollector.Esi;

public sealed class EsiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly EsiRateLimiter _rateLimiter;
    private readonly EsiErrorTracker _errorTracker;
    private readonly ILogger<EsiClient> _logger;

    // Maps path → known rate limit group so throttling can apply before the request fires.
    private readonly ConcurrentDictionary<string, string> _pathGroupCache = new();

    public EsiClient(
        IHttpClientFactory httpClientFactory,
        EsiRateLimiter rateLimiter,
        EsiErrorTracker errorTracker,
        ILogger<EsiClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _rateLimiter = rateLimiter;
        _errorTracker = errorTracker;
        _logger = logger;
    }

    public async Task<EsiResponse<T>> GetAsync<T>(
        string path,
        string? etag = null,
        int attempt = 0,
        CancellationToken ct = default)
    {
        var group = _pathGroupCache.GetValueOrDefault(path, "default");

        await _rateLimiter.ThrottleAsync(group, ct);
        await _errorTracker.ThrottleIfNeededAsync(ct);

        var client = _httpClientFactory.CreateClient("ESI");
        using var request = new HttpRequestMessage(HttpMethod.Get, path);

        if (etag is not null)
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HTTP request failed for {Path}", path);
            throw;
        }

        UpdateTrackers(path, response);

        var expires = response.Content.Headers.Expires;
        var lastModified = response.Content.Headers.LastModified;
        var responseETag = response.Headers.ETag?.ToString();
        var rateLimitGroup = _pathGroupCache.GetValueOrDefault(path, "default");
        int remaining = TryGetInt(response, "X-Ratelimit-Remaining");
        int used = TryGetInt(response, "X-Ratelimit-Used");

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(10);
            _logger.LogWarning(
                "429 on {Path} — waiting {RetryAfter}s (attempt {Attempt})",
                path, (int)retryAfter.TotalSeconds, attempt + 1);

            if (attempt < 3)
            {
                await Task.Delay(retryAfter, ct);
                return await GetAsync<T>(path, etag, attempt + 1, ct);
            }

            _logger.LogError("Exhausted retries for {Path} after 429s", path);
            return new EsiResponse<T>(default, response.StatusCode, expires, lastModified, responseETag, remaining, used, rateLimitGroup);
        }

        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            _logger.LogDebug("304 Not Modified for {Path}", path);
            return new EsiResponse<T>(default, response.StatusCode, expires, lastModified, responseETag, remaining, used, rateLimitGroup);
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("ESI returned {StatusCode} for {Path}", (int)response.StatusCode, path);
            return new EsiResponse<T>(default, response.StatusCode, expires, lastModified, responseETag, remaining, used, rateLimitGroup);
        }

        var data = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
        return new EsiResponse<T>(data, response.StatusCode, expires, lastModified, responseETag, remaining, used, rateLimitGroup);
    }

    private void UpdateTrackers(string path, HttpResponseMessage response)
    {
        _errorTracker.UpdateFromHeaders(response.Headers);

        if (!response.Headers.TryGetValues("X-Ratelimit-Group", out var groupValues))
            return;

        var group = groupValues.FirstOrDefault();
        if (group is null) return;

        _pathGroupCache[path] = group;

        int remaining = TryGetInt(response, "X-Ratelimit-Remaining");
        int used = TryGetInt(response, "X-Ratelimit-Used");
        int total = 0;
        int windowSeconds = 0;

        if (response.Headers.TryGetValues("X-Ratelimit-Limit", out var limitValues))
        {
            var limitHeader = limitValues.FirstOrDefault() ?? string.Empty;
            (total, windowSeconds) = EsiRateLimiter.ParseLimitHeader(limitHeader);
        }

        _rateLimiter.Update(group, remaining, used, total, windowSeconds);
    }

    private static int TryGetInt(HttpResponseMessage response, string header)
    {
        if (response.Headers.TryGetValues(header, out var values) &&
            int.TryParse(values.FirstOrDefault(), out var parsed))
            return parsed;
        return 0;
    }
}
