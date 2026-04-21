namespace EveStatsCollector.Esi;

public sealed class EsiRateLimitHandler : DelegatingHandler
{
    private readonly EsiRateLimiter _rateLimiter;
    private readonly EsiErrorTracker _errorTracker;

    public EsiRateLimitHandler(EsiRateLimiter rateLimiter, EsiErrorTracker errorTracker)
    {
        _rateLimiter = rateLimiter;
        _errorTracker = errorTracker;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken ct)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;

        await _rateLimiter.ThrottleAsync(_rateLimiter.GetGroup(path), ct);
        await _errorTracker.ThrottleIfNeededAsync(ct);

        var response = await base.SendAsync(request, ct);

        UpdateTrackers(path, response);

        return response;
    }

    private void UpdateTrackers(string path, HttpResponseMessage response)
    {
        _errorTracker.UpdateFromHeaders(response.Headers);

        if (!response.Headers.TryGetValues("X-Ratelimit-Group", out var groupValues))
            return;

        var group = groupValues.FirstOrDefault();
        if (group is null) return;

        _rateLimiter.RegisterPath(path, group);

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
