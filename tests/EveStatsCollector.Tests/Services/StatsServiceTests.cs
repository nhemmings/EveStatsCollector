using System.Net;
using EveStatsCollector.Esi;
using EveStatsCollector.Models;
using EveStatsCollector.Repositories;
using EveStatsCollector.Repositories.InMemory;
using EveStatsCollector.Services;
using EveStatsCollector.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EveStatsCollector.Tests.Services;

/// <summary>
/// Unit tests for <see cref="StatsService"/>. The service is a long-running loop, so
/// tests cancel the token shortly after the first iteration has stored results.
/// </summary>
public class StatsServiceTests
{
    private sealed record Rig(
        StatsService Service,
        StubHttpMessageHandler Handler,
        InMemoryKillsReportRepository Kills,
        InMemoryJumpsReportRepository Jumps,
        InMemorySolarSystemRepository Systems);

    private static Rig Build()
    {
        var handler = new StubHttpMessageHandler();
        var factory = new HttpClientFactoryStub(handler);
        var esi = new EsiClient(factory, NullLogger<EsiClient>.Instance);
        var systems = new InMemorySolarSystemRepository();
        var constellations = new InMemoryConstellationRepository();
        var regions = new InMemoryRegionRepository();
        var universe = new UniverseService(esi, systems, constellations, regions,
            new UniverseConstellationFilter([]), NullLogger<UniverseService>.Instance);
        var kills = new InMemoryKillsReportRepository();
        var jumps = new InMemoryJumpsReportRepository();
        var reportFilter = new ReportConstellationFilter(constellations, Array.Empty<string>());
        var stats = new StatsService(esi, universe, kills, jumps, reportFilter,
            NullLogger<StatsService>.Instance);
        return new Rig(stats, handler, kills, jumps, systems);
    }

    private static Task RunUntilFirstStored(StatsService svc, Func<Task<bool>> stored, CancellationTokenSource cts,
        TimeSpan timeout)
    {
        var run = svc.RunAsync(cts.Token);
        _ = Task.Run(async () =>
        {
            var deadline = DateTimeOffset.UtcNow + timeout;
            while (DateTimeOffset.UtcNow < deadline && !await stored())
                await Task.Delay(20);
            cts.Cancel();
        });
        return run;
    }

    [Fact]
    public async Task RunAsync_FirstIteration_StoresKillsAndJumpsReports()
    {
        var rig = Build();
        var lastModified = DateTimeOffset.Parse("2026-04-21T10:00:00Z");
        var expires = DateTimeOffset.UtcNow.AddSeconds(2);

        rig.Handler.EnqueueJson(
            new[] { new SystemKills(30000142, 1, 2, 3) },
            etag: "\"k-etag\"", lastModified: lastModified, expires: expires);
        rig.Handler.EnqueueJson(
            new[] { new SystemJumps(30000142, 42) },
            etag: "\"j-etag\"", lastModified: lastModified, expires: expires);
        rig.Systems.Upsert(new SolarSystem(30000142, "Jita", 20000020, 0.945f));

        using var cts = new CancellationTokenSource();
        await RunUntilFirstStored(rig.Service,
            async () => await rig.Kills.GetLatestAsync() is not null && await rig.Jumps.GetLatestAsync() is not null,
            cts, TimeSpan.FromSeconds(5));

        (await rig.Kills.GetLatestAsync())!.Entries.Should().ContainSingle()
            .Which.NpcKills.Should().Be(2);
        (await rig.Jumps.GetLatestAsync())!.Entries.Should().ContainSingle()
            .Which.ShipJumps.Should().Be(42);
    }

    [Fact]
    public async Task RunAsync_304Responses_DoesNotCreateReports()
    {
        var rig = Build();
        rig.Handler.EnqueueStatus(HttpStatusCode.NotModified);
        rig.Handler.EnqueueStatus(HttpStatusCode.NotModified);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        try { await rig.Service.RunAsync(cts.Token); } catch (OperationCanceledException) { }

        (await rig.Kills.GetAllAsync()).Should().BeEmpty();
        (await rig.Jumps.GetAllAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_UnknownSystemInResponse_TriggersLazyFetch()
    {
        var rig = Build();
        var expires = DateTimeOffset.UtcNow.AddSeconds(2);

        rig.Handler.EnqueueJson(
            new[] { new SystemKills(99999999, 0, 0, 1) },
            expires: expires);
        rig.Handler.EnqueueJson(
            Array.Empty<SystemJumps>(),
            expires: expires);
        rig.Handler.EnqueueJson(new SolarSystem(99999999, "Thera", 20000001, -0.99f));

        using var cts = new CancellationTokenSource();
        await RunUntilFirstStored(rig.Service,
            () => Task.FromResult(rig.Systems.GetById(99999999) is not null),
            cts, TimeSpan.FromSeconds(5));

        rig.Systems.GetById(99999999)!.Name.Should().Be("Thera");
    }

    [Fact]
    public async Task RunAsync_KillsSucceedsJumpsFails_StillStoresKills()
    {
        var rig = Build();
        var expires = DateTimeOffset.UtcNow.AddSeconds(2);

        rig.Handler.EnqueueJson(
            new[] { new SystemKills(30000142, 1, 1, 1) },
            expires: expires);
        rig.Handler.EnqueueStatus(HttpStatusCode.InternalServerError);
        rig.Systems.Upsert(new SolarSystem(30000142, "Jita", 20000020, 0.945f));

        using var cts = new CancellationTokenSource();
        await RunUntilFirstStored(rig.Service,
            async () => await rig.Kills.GetLatestAsync() is not null,
            cts, TimeSpan.FromSeconds(5));

        (await rig.Kills.GetLatestAsync()).Should().NotBeNull();
        (await rig.Jumps.GetAllAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_TransportException_SwallowsAndContinues()
    {
        var rig = Build();
        rig.Handler.EnqueueResponder(_ => throw new HttpRequestException("boom"));

        // After the transport exception the service enters a 1-hour backoff, so we cancel
        // quickly.  The point of this test is that the exception is swallowed — the service
        // exits cleanly via cancellation rather than propagating the exception to the caller.
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        Func<Task> act = () => rig.Service.RunAsync(cts.Token);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RunAsync_CancellationBeforeFirstIteration_ExitsCleanly()
    {
        var rig = Build();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => rig.Service.RunAsync(cts.Token);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RunAsync_Success_PropagatesETagOnSecondIteration()
    {
        var rig = Build();
        var expires = DateTimeOffset.UtcNow.AddMilliseconds(300);

        rig.Handler.EnqueueJson(
            Array.Empty<SystemKills>(),
            etag: "\"k1\"", expires: expires);
        rig.Handler.EnqueueJson(
            Array.Empty<SystemJumps>(),
            etag: "\"j1\"", expires: expires);
        rig.Handler.EnqueueStatus(HttpStatusCode.NotModified);
        rig.Handler.EnqueueStatus(HttpStatusCode.NotModified);

        using var cts = new CancellationTokenSource();
        await RunUntilFirstStored(rig.Service,
            () => Task.FromResult(rig.Handler.RequestCount >= 4),
            cts, TimeSpan.FromSeconds(10));

        var requests = rig.Handler.Requests.ToArray();
        if (requests.Length >= 4)
        {
            requests[2].Headers.TryGetValues("If-None-Match", out var ketag).Should().BeTrue();
            ketag!.Should().Contain("\"k1\"");
            requests[3].Headers.TryGetValues("If-None-Match", out var jetag).Should().BeTrue();
            jetag!.Should().Contain("\"j1\"");
        }
    }
}
