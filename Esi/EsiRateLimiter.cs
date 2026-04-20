using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace EveStatsCollector.Esi;

public sealed class EsiRateLimiter
{
    private readonly ILogger<EsiRateLimiter> _logger;
    private readonly ConcurrentDictionary<string, RateLimitState> _states = new();

    public EsiRateLimiter(ILogger<EsiRateLimiter> logger) => _logger = logger;

    public async Task ThrottleAsync(string group, CancellationToken ct)
    {
        if (!_states.TryGetValue(group, out var state))
            return;

        var delay = CalculateDelay(state);

        if (delay > TimeSpan.Zero)
        {
            _logger.LogDebug(
                "Rate limit throttle on group {Group}: delaying {DelayMs}ms",
                group, (int)delay.TotalMilliseconds);
            await Task.Delay(delay, ct);
        }
    }

    public void Update(string group, int remaining, int used, int total, int windowSeconds)
    {
        _states[group] = new RateLimitState(remaining, used, total, windowSeconds);

        var pct = total > 0 ? (double)remaining / total * 100 : 100;
        _logger.LogDebug(
            "Rate limit [{Group}]: {Remaining}/{Total} tokens ({Pct:F0}%), window {Window}s",
            group, remaining, total, pct, windowSeconds);
    }

    private static TimeSpan CalculateDelay(RateLimitState state)
    {
        if (state.Total == 0)
            return TimeSpan.Zero;

        double pct = (double)state.Remaining / state.Total;

        // CCP asks apps not to operate at the limit; stay comfortably above it.
        // Graduated delays give the floating window time to restore tokens.
        return pct switch
        {
            > 0.50 => TimeSpan.Zero,
            > 0.25 => TimeSpan.FromMilliseconds(500),
            > 0.10 => TimeSpan.FromSeconds(2),
            > 0.00 => TimeSpan.FromSeconds(5 + (0.10 - pct) / 0.10 * 5), // 5–10s
            _       => TimeSpan.FromSeconds(Math.Max(state.WindowSeconds / 4.0, 10))
        };
    }

    // Parses "150/15m" → (150, 900)
    public static (int Total, int WindowSeconds) ParseLimitHeader(string header)
    {
        var parts = header.Split('/');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var total))
            return (0, 0);

        var windowStr = parts[1];
        int windowSeconds = windowStr switch
        {
            _ when windowStr.EndsWith('m') && int.TryParse(windowStr[..^1], out var m) => m * 60,
            _ when windowStr.EndsWith('s') && int.TryParse(windowStr[..^1], out var s) => s,
            _ when windowStr.EndsWith('h') && int.TryParse(windowStr[..^1], out var h) => h * 3600,
            _ => 0
        };

        return (total, windowSeconds);
    }

    private record RateLimitState(int Remaining, int Used, int Total, int WindowSeconds);
}
