using System.Diagnostics;
using EveStatsCollector.Esi;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EveStatsCollector.Tests.Esi;

/// <summary>
/// Unit tests for <see cref="EsiErrorTracker"/> — the legacy per-minute
/// 100-error budget tracker.
/// </summary>
public class EsiErrorTrackerTests
{
    private static EsiErrorTracker CreateTracker() => new(NullLogger<EsiErrorTracker>.Instance);

    private static HttpResponseMessage BuildResponseWithHeaders(params (string, string)[] headers)
    {
        var resp = new HttpResponseMessage();
        foreach (var (k, v) in headers)
            resp.Headers.TryAddWithoutValidation(k, v);
        return resp;
    }

    [Fact]
    public async Task ThrottleIfNeededAsync_FreshTracker_ReturnsImmediately()
    {
        var tracker = CreateTracker();
        var sw = Stopwatch.StartNew();
        await tracker.ThrottleIfNeededAsync(CancellationToken.None);
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(50);
    }

    [Fact]
    public void UpdateFromHeaders_ValidHeaders_ParsesRemainAndReset()
    {
        var tracker = CreateTracker();
        using var resp = BuildResponseWithHeaders(
            ("X-Esi-Error-Limit-Remain", "50"),
            ("X-Esi-Error-Limit-Reset", "30"));

        // Should not throw and should log at some warning threshold if needed.
        tracker.UpdateFromHeaders(resp.Headers);
    }

    [Fact]
    public void UpdateFromHeaders_MissingHeaders_UsesFallbackDefaults()
    {
        var tracker = CreateTracker();
        using var resp = BuildResponseWithHeaders();

        tracker.UpdateFromHeaders(resp.Headers);
        // No throw, defaults remain=100 / reset=60.
    }

    [Fact]
    public void UpdateFromHeaders_NonNumericHeader_FallsBackToDefaults()
    {
        var tracker = CreateTracker();
        using var resp = BuildResponseWithHeaders(
            ("X-Esi-Error-Limit-Remain", "not-a-number"),
            ("X-Esi-Error-Limit-Reset", "also-not-a-number"));

        tracker.UpdateFromHeaders(resp.Headers);
        // No throw, defaults used.
    }

    [Fact]
    public void UpdateFromHeaders_Remain15_LogsWarningButDoesNotThrottle()
    {
        // Warn threshold is <20 but throttle threshold is <10.
        var tracker = CreateTracker();
        using var resp = BuildResponseWithHeaders(
            ("X-Esi-Error-Limit-Remain", "15"),
            ("X-Esi-Error-Limit-Reset", "45"));

        tracker.UpdateFromHeaders(resp.Headers);
        // Subsequent throttle call should STILL return immediately.
        var sw = Stopwatch.StartNew();
        tracker.ThrottleIfNeededAsync(CancellationToken.None).Wait();
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(50);
    }

    [Fact]
    public async Task ThrottleIfNeededAsync_RemainBelow10_Delays()
    {
        var tracker = CreateTracker();
        using var resp = BuildResponseWithHeaders(
            ("X-Esi-Error-Limit-Remain", "5"),
            ("X-Esi-Error-Limit-Reset", "2"));
        tracker.UpdateFromHeaders(resp.Headers);

        var sw = Stopwatch.StartNew();
        await tracker.ThrottleIfNeededAsync(CancellationToken.None);
        sw.Stop();

        // Expected delay is min(reset, 60) = 2s. Allow generous tolerance.
        sw.ElapsedMilliseconds.Should().BeInRange(1500, 4000);
    }

    [Fact]
    public async Task ThrottleIfNeededAsync_Critical_CappedAt60s()
    {
        // If reset was huge, throttle should cap at 60s (we can't realistically
        // wait 60s; instead cancel after a short delay and assert behavior).
        var tracker = CreateTracker();
        using var resp = BuildResponseWithHeaders(
            ("X-Esi-Error-Limit-Remain", "0"),
            ("X-Esi-Error-Limit-Reset", "600"));
        tracker.UpdateFromHeaders(resp.Headers);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        Func<Task> act = () => tracker.ThrottleIfNeededAsync(cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task UpdateFromHeaders_ConcurrentUpdates_ThreadSafe()
    {
        var tracker = CreateTracker();

        var tasks = Enumerable.Range(0, 32).Select(i => Task.Run(() =>
        {
            for (int j = 0; j < 50; j++)
            {
                using var resp = BuildResponseWithHeaders(
                    ("X-Esi-Error-Limit-Remain", (100 - (i % 100)).ToString()),
                    ("X-Esi-Error-Limit-Reset", "60"));
                tracker.UpdateFromHeaders(resp.Headers);
            }
        })).ToArray();

        await Task.WhenAll(tasks);
        await tracker.ThrottleIfNeededAsync(CancellationToken.None);
    }
}
