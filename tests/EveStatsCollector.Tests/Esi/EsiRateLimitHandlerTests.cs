using System.Net;
using EveStatsCollector.Esi;
using EveStatsCollector.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EveStatsCollector.Tests.Esi;

/// <summary>
/// Unit tests for <see cref="EsiRateLimitHandler"/>. Verifies it wires request/response
/// flow through both the rate limiter and the error tracker, and that it parses
/// rate-limit response headers correctly.
/// </summary>
public class EsiRateLimitHandlerTests
{
    private static (HttpClient Client, EsiRateLimiter Limiter, EsiErrorTracker ErrorTracker, StubHttpMessageHandler Stub) Build()
    {
        var limiter = new EsiRateLimiter(NullLogger<EsiRateLimiter>.Instance);
        var errorTracker = new EsiErrorTracker(NullLogger<EsiErrorTracker>.Instance);
        var stub = new StubHttpMessageHandler();
        var handler = new EsiRateLimitHandler(limiter, errorTracker) { InnerHandler = stub };
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://esi.evetech.net/latest/") };
        return (client, limiter, errorTracker, stub);
    }

    [Fact]
    public async Task SendAsync_ResponseWithRatelimitHeaders_RegistersPathAndUpdatesLimiter()
    {
        var (client, limiter, _, stub) = Build();
        stub.EnqueueStatus(HttpStatusCode.OK, headers: new Dictionary<string, string>
        {
            ["X-Ratelimit-Group"]     = "universe-read",
            ["X-Ratelimit-Remaining"] = "80",
            ["X-Ratelimit-Used"]      = "20",
            ["X-Ratelimit-Limit"]     = "100/60s",
        });

        var response = await client.GetAsync("universe/regions/");
        response.EnsureSuccessStatusCode();

        limiter.GetGroup("/latest/universe/regions/").Should().Be("universe-read");
    }

    [Fact]
    public async Task SendAsync_ResponseWithoutGroupHeader_LeavesGroupAtDefault()
    {
        var (client, limiter, _, stub) = Build();
        stub.EnqueueStatus(HttpStatusCode.OK);

        await client.GetAsync("universe/regions/");

        limiter.GetGroup("/latest/universe/regions/").Should().Be("default");
    }

    [Fact]
    public async Task SendAsync_ResponseWithErrorLimitHeaders_UpdatesErrorTracker()
    {
        var (client, _, errorTracker, stub) = Build();

        // Make throttler see critical state, and verify a subsequent throttle call
        // actually delays (proves headers reached the error tracker).
        stub.EnqueueStatus(HttpStatusCode.OK, headers: new Dictionary<string, string>
        {
            ["X-Esi-Error-Limit-Remain"] = "3",
            ["X-Esi-Error-Limit-Reset"]  = "2",
        });

        await client.GetAsync("universe/regions/");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await errorTracker.ThrottleIfNeededAsync(CancellationToken.None);
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeInRange(1500, 4000);
    }

    [Fact]
    public async Task SendAsync_MalformedRatelimitLimit_DoesNotThrow()
    {
        var (client, _, _, stub) = Build();
        stub.EnqueueStatus(HttpStatusCode.OK, headers: new Dictionary<string, string>
        {
            ["X-Ratelimit-Group"]  = "g",
            ["X-Ratelimit-Limit"]  = "garbage",
        });

        Func<Task> act = async () => await client.GetAsync("universe/regions/");
        await act.Should().NotThrowAsync();
    }
}
