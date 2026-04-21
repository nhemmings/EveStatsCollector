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
    private readonly ILogger<EsiClient> _logger;

    public EsiClient(IHttpClientFactory httpClientFactory, ILogger<EsiClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<EsiResponse<T>> GetAsync<T>(
        string path,
        string? etag = null,
        int attempt = 0,
        CancellationToken ct = default)
    {
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

        var expires = response.Content.Headers.Expires;
        var lastModified = response.Content.Headers.LastModified;
        var responseETag = response.Headers.ETag?.ToString();

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
            return new EsiResponse<T>(default, response.StatusCode, expires, lastModified, responseETag);
        }

        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            _logger.LogDebug("304 Not Modified for {Path}", path);
            return new EsiResponse<T>(default, response.StatusCode, expires, lastModified, responseETag);
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("ESI returned {StatusCode} for {Path}", (int)response.StatusCode, path);
            return new EsiResponse<T>(default, response.StatusCode, expires, lastModified, responseETag);
        }

        var data = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
        return new EsiResponse<T>(data, response.StatusCode, expires, lastModified, responseETag);
    }
}
