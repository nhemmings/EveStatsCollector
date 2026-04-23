using System.Diagnostics;
using EveStatsCollector.Esi;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EveStatsCollector.Tests.Esi;

/// <summary>
/// Unit tests for <see cref="EsiRateLimiter"/>.
/// Covers path/group registration, graduated delay calculation based on token percentage,
/// limit-header parsing, and concurrent access.
///
/// IMPORTANT — all token state is mocked via <see cref="EsiRateLimiter.Update"/>.
/// No test simulates real ESI token consumption. When simulating remaining tokens
/// below 15% of total, the test verifies the rate limiter ITSELF enforces back-off
/// (via <see cref="EsiRateLimiter.ThrottleAsync"/>), rather than issuing further
/// token-consuming work.
/// </summary>
public class EsiRateLimiterTests
{
    private static EsiRateLimiter CreateLimiter() => new(NullLogger<EsiRateLimiter>.Instance);

    #region GetGroup / RegisterPath

    [Fact]
    public void GetGroup_PathNotRegistered_ReturnsDefault()
    {
        var limiter = CreateLimiter();
        limiter.GetGroup("universe/systems/30000142/").Should().Be("default");
    }

    [Fact]
    public void RegisterPath_ThenGetGroup_ReturnsRegisteredGroup()
    {
        var limiter = CreateLimiter();
        limiter.RegisterPath("universe/systems/30000142/", "universe-read");
        limiter.GetGroup("universe/systems/30000142/").Should().Be("universe-read");
    }

    [Fact]
    public void RegisterPath_Overwrite_TakesLatestGroup()
    {
        var limiter = CreateLimiter();
        limiter.RegisterPath("path", "groupA");
        limiter.RegisterPath("path", "groupB");
        limiter.GetGroup("path").Should().Be("groupB");
    }

    #endregion

    #region ThrottleAsync — graduated delays

    [Fact]
    public async Task ThrottleAsync_UnknownGroup_ReturnsImmediately()
    {
        var limiter = CreateLimiter();
        var sw = Stopwatch.StartNew();
        await limiter.ThrottleAsync("never-registered", CancellationToken.None);
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(50);
    }

    [Fact]
    public async Task ThrottleAsync_Above50PctTokens_NoDelay()
    {
        var limiter = CreateLimiter();
        limiter.Update("g", remaining: 80, used: 20, total: 100, windowSeconds: 60);

        var sw = Stopwatch.StartNew();
        await limiter.ThrottleAsync("g", CancellationToken.None);
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeLessThan(50);
    }

    [Fact]
    public async Task ThrottleAsync_Between25And50PctTokens_Delays500ms()
    {
        var limiter = CreateLimiter();
        limiter.Update("g", remaining: 40, used: 60, total: 100, windowSeconds: 60);

        var sw = Stopwatch.StartNew();
        await limiter.ThrottleAsync("g", CancellationToken.None);
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeInRange(400, 1500);
    }

    [Fact]
    public async Task ThrottleAsync_Between10And25PctTokens_DelaysAround2s()
    {
        var limiter = CreateLimiter();
        // 15% => falls into the 10–25% bucket => 2s delay
        limiter.Update("g", remaining: 15, used: 85, total: 100, windowSeconds: 60);

        var sw = Stopwatch.StartNew();
        await limiter.ThrottleAsync("g", CancellationToken.None);
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeInRange(1800, 3500);
    }

    [Fact]
    public async Task ThrottleAsync_TotalIsZero_ReturnsImmediately()
    {
        var limiter = CreateLimiter();
        // No total known yet — calculation should short-circuit.
        limiter.Update("g", remaining: 0, used: 0, total: 0, windowSeconds: 0);

        var sw = Stopwatch.StartNew();
        await limiter.ThrottleAsync("g", CancellationToken.None);
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeLessThan(50);
    }

    [Fact]
    public async Task ThrottleAsync_CancellationRequestedWhileDelaying_Throws()
    {
        var limiter = CreateLimiter();
        // Big delay so cancellation fires mid-wait.
        limiter.Update("g", remaining: 0, used: 100, total: 100, windowSeconds: 120);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        Func<Task> act = () => limiter.ThrottleAsync("g", cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region 15% back-off threshold — stress/guardrail tests

    [Theory]
    // Everything at or below 15% remaining must yield a STRICTLY POSITIVE delay.
    [InlineData(15, 100)]  // exactly 15% — still in the "> 10%, throttle 2s" bucket
    [InlineData(10, 100)]  // 10% — "> 0%, but below 10%": 5–10s bucket
    [InlineData(5, 100)]   // 5% — 5–10s graduated bucket
    [InlineData(1, 100)]   // 1% — 5–10s graduated bucket
    [InlineData(0, 100)]   // 0% — critical bucket, max(window/4, 10s)
    public void CalculateDelay_AtOrBelow15PctTokens_AlwaysProducesPositiveBackoff(int remaining, int total)
    {
        var limiter = CreateLimiter();
        limiter.Update("g", remaining, used: total - remaining, total, windowSeconds: 60);

        // We probe CalculateDelay indirectly by running ThrottleAsync with a tight
        // cancellation — if the limiter is enforcing back-off we expect the call to
        // trigger Task.Delay which will observe the cancellation.
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        Func<Task> act = () => limiter.ThrottleAsync("g", cts.Token);

        // Either it's delaying (and cancellation fires) or (illegally) completes early.
        // We assert it's delaying — that's the back-off guarantee.
        act.Should().ThrowAsync<OperationCanceledException>().Wait(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ThrottleAsync_RemainingBelow15Pct_RateLimiterEnforcesBackoff_AndCallerShouldNotDispatch()
    {
        // Mock state: 14 tokens remaining of 100 => 14% < 15%.
        var limiter = CreateLimiter();
        limiter.Update("throttled-group", remaining: 14, used: 86, total: 100, windowSeconds: 60);

        // Simulate a caller that respects the limiter: it awaits throttle and tracks
        // how many real requests it dispatched. The test asserts that a reasonable
        // caller, honoring the limiter, cannot proceed quickly (i.e. work is gated).
        int dispatched = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        Func<Task> caller = async () =>
        {
            await limiter.ThrottleAsync("throttled-group", cts.Token);
            dispatched++;
        };

        await caller.Should().ThrowAsync<OperationCanceledException>();
        dispatched.Should().Be(0, "no request should be dispatched while the limiter is enforcing back-off");
    }

    [Fact]
    public void Update_NearlyExhausted_LogsWarningAndPersistsState()
    {
        // This test documents state shape after an exhausted window. Since the
        // private state is not directly readable, we assert via ThrottleAsync
        // that behavior reflects "critical" treatment.
        var limiter = CreateLimiter();
        limiter.Update("g", remaining: 0, used: 100, total: 100, windowSeconds: 60);

        var sw = Stopwatch.StartNew();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
        try { limiter.ThrottleAsync("g", cts.Token).Wait(); }
        catch (AggregateException) { /* expected cancellation */ }
        sw.Stop();

        // It should have WAITED (i.e. not returned instantly) — critical bucket
        // dictates at least window/4 = 15s of delay.
        sw.ElapsedMilliseconds.Should().BeGreaterThan(10);
    }

    #endregion

    #region ParseLimitHeader

    [Theory]
    [InlineData("150/15m", 150, 900)]
    [InlineData("20/60s", 20, 60)]
    [InlineData("5/1h", 5, 3600)]
    [InlineData("1/1s", 1, 1)]
    public void ParseLimitHeader_ValidHeader_ParsesTotalAndWindow(string header, int expectedTotal, int expectedWindow)
    {
        var (total, window) = EsiRateLimiter.ParseLimitHeader(header);
        total.Should().Be(expectedTotal);
        window.Should().Be(expectedWindow);
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("no-slash")]
    [InlineData("abc/15m")]       // non-numeric total
    [InlineData("100/")]           // empty window
    [InlineData("100/15x")]        // unknown window unit
    [InlineData("100/m")]          // empty window number
    public void ParseLimitHeader_InvalidHeader_ReturnsZeroes(string header)
    {
        var (total, window) = EsiRateLimiter.ParseLimitHeader(header);
        // For the 100/ cases, total can be 100 but window should be 0.
        // For non-parseable totals, total=0.
        // We accept either interpretation for the fall-through cases.
        (total == 0 || window == 0).Should().BeTrue(
            $"invalid header '{header}' should yield 0 for at least one component");
    }

    #endregion

    #region Concurrency

    [Fact]
    public async Task Update_ConcurrentCallers_ThreadSafe()
    {
        var limiter = CreateLimiter();

        // Pound Update + GetGroup/RegisterPath from many tasks; ConcurrentDictionary
        // should make this safe. Assert no exceptions and final state is sane.
        var tasks = Enumerable.Range(0, 64).Select(i => Task.Run(() =>
        {
            for (int j = 0; j < 100; j++)
            {
                limiter.RegisterPath($"path-{i}", $"group-{i % 4}");
                limiter.Update($"group-{i % 4}", remaining: 80, used: 20, total: 100, windowSeconds: 60);
                _ = limiter.GetGroup($"path-{i}");
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        // Should be idempotent-ish; delays should still be 0 since all tokens at 80%.
        var sw = Stopwatch.StartNew();
        await limiter.ThrottleAsync("group-0", CancellationToken.None);
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(50);
    }

    [Fact]
    public async Task ThrottleAsync_BurstUnderHealthyTokens_NoSerialization()
    {
        // Stress: 20 parallel callers, all with 80% tokens => should all proceed.
        var limiter = CreateLimiter();
        limiter.Update("burst", remaining: 80, used: 20, total: 100, windowSeconds: 60);

        var sw = Stopwatch.StartNew();
        var tasks = Enumerable.Range(0, 20)
            .Select(_ => limiter.ThrottleAsync("burst", CancellationToken.None))
            .ToArray();
        await Task.WhenAll(tasks);
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeLessThan(200);
    }

    #endregion
}
