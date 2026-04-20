using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;

namespace EveStatsCollector.Esi;

// Tracks the legacy per-minute error limit still active on routes not yet
// migrated to the new floating-window rate limiter.
public sealed class EsiErrorTracker
{
    private readonly ILogger<EsiErrorTracker> _logger;
    private readonly object _lock = new();
    private int _remain = 100;
    private int _resetSeconds = 60;

    public EsiErrorTracker(ILogger<EsiErrorTracker> logger) => _logger = logger;

    public void UpdateFromHeaders(HttpResponseHeaders headers)
    {
        int remain = TryGet(headers, "X-Esi-Error-Limit-Remain", 100);
        int reset = TryGet(headers, "X-Esi-Error-Limit-Reset", 60);

        lock (_lock)
        {
            _remain = remain;
            _resetSeconds = reset;
        }

        if (remain < 20)
            _logger.LogWarning("Legacy error limit low: {Remain} remaining, resets in {Reset}s", remain, reset);
    }

    public async Task ThrottleIfNeededAsync(CancellationToken ct)
    {
        int remain, reset;
        lock (_lock)
        {
            remain = _remain;
            reset = _resetSeconds;
        }

        if (remain < 10)
        {
            var wait = Math.Min(reset, 60);
            _logger.LogWarning(
                "Legacy error limit critical ({Remain} remaining). Pausing {Wait}s before next request.",
                remain, wait);
            await Task.Delay(TimeSpan.FromSeconds(wait), ct);
        }
    }

    private static int TryGet(HttpResponseHeaders headers, string name, int fallback)
    {
        if (headers.TryGetValues(name, out var values) &&
            int.TryParse(values.FirstOrDefault(), out var parsed))
            return parsed;
        return fallback;
    }
}
