using System.Net;
using EveStatsCollector.Esi;
using EveStatsCollector.Models;
using EveStatsCollector.Repositories.InMemory;
using EveStatsCollector.Services;
using EveStatsCollector.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EveStatsCollector.Tests.Services;

/// <summary>
/// Unit tests for <see cref="UniverseService"/>. Because both <see cref="UniverseService"/>
/// and <see cref="EsiClient"/> are sealed, we construct a real EsiClient backed by a
/// scripted HttpMessageHandler and real in-memory repositories.
/// </summary>
public class UniverseServiceTests
{
    private static (UniverseService Service, StubHttpMessageHandler Handler,
        InMemorySolarSystemRepository Systems, InMemoryConstellationRepository Constellations,
        InMemoryRegionRepository Regions) Build()
    {
        var handler = new StubHttpMessageHandler();
        var factory = new HttpClientFactoryStub(handler);
        var esi = new EsiClient(factory, NullLogger<EsiClient>.Instance);
        var systems = new InMemorySolarSystemRepository();
        var constellations = new InMemoryConstellationRepository();
        var regions = new InMemoryRegionRepository();
        var svc = new UniverseService(esi, systems, constellations, regions,
            NullLogger<UniverseService>.Instance);
        return (svc, handler, systems, constellations, regions);
    }

    [Fact]
    public async Task InitializeAsync_FetchesRegionsConstellationsAndSystems()
    {
        var (svc, handler, systems, constellations, regions) = Build();

        // Region list + detail
        handler.EnqueueJson(new[] { 10000001 });
        handler.EnqueueJson(new Region(10000001, "Derelik", new[] { 20000001 }));
        // Constellation list + detail
        handler.EnqueueJson(new[] { 20000001 });
        handler.EnqueueJson(new Constellation(20000001, "San Matar", 10000001, new[] { 30000001 }));
        // System list + detail
        handler.EnqueueJson(new[] { 30000001 });
        handler.EnqueueJson(new SolarSystem(30000001, "Tanoo", 20000001, 0.7f));

        await svc.InitializeAsync(CancellationToken.None);

        regions.GetAll().Should().ContainSingle().Which.Name.Should().Be("Derelik");
        constellations.GetAll().Should().ContainSingle().Which.Name.Should().Be("San Matar");
        systems.GetAll().Should().ContainSingle().Which.Name.Should().Be("Tanoo");
    }

    [Fact]
    public async Task InitializeAsync_EmptyIdList_StoresNothing()
    {
        var (svc, handler, systems, constellations, regions) = Build();

        handler.EnqueueJson(Array.Empty<int>());
        handler.EnqueueJson(Array.Empty<int>());
        handler.EnqueueJson(Array.Empty<int>());

        await svc.InitializeAsync(CancellationToken.None);

        regions.GetAll().Should().BeEmpty();
        constellations.GetAll().Should().BeEmpty();
        systems.GetAll().Should().BeEmpty();
    }

    [Fact]
    public async Task InitializeAsync_IdAlreadyLoaded_SkipsDetailFetch()
    {
        var (svc, handler, systems, _, _) = Build();
        systems.Upsert(new SolarSystem(30000001, "Tanoo", 20000001, 0.7f));

        handler.EnqueueJson(Array.Empty<int>());        // regions list
        handler.EnqueueJson(Array.Empty<int>());        // constellations list
        handler.EnqueueJson(new[] { 30000001 });        // systems list — id already present

        await svc.InitializeAsync(CancellationToken.None);

        // Only 3 requests total — no detail fetch for the already-known system.
        handler.RequestCount.Should().Be(3);
    }

    [Fact]
    public async Task InitializeAsync_SecondCall_UsesETagAndReceives304()
    {
        var (svc, handler, _, _, _) = Build();

        // First pass — return ETags.
        handler.EnqueueJson(Array.Empty<int>(), etag: "\"r-etag\"");
        handler.EnqueueJson(Array.Empty<int>(), etag: "\"c-etag\"");
        handler.EnqueueJson(Array.Empty<int>(), etag: "\"s-etag\"");
        await svc.InitializeAsync(CancellationToken.None);

        // Second pass — expect If-None-Match with the stored ETag and return 304.
        handler.EnqueueStatus(HttpStatusCode.NotModified);
        handler.EnqueueStatus(HttpStatusCode.NotModified);
        handler.EnqueueStatus(HttpStatusCode.NotModified);
        await svc.InitializeAsync(CancellationToken.None);

        var requests = handler.Requests.ToArray();
        var secondRegionRequest = requests[3];
        secondRegionRequest.Headers.TryGetValues("If-None-Match", out var etagValues).Should().BeTrue();
        etagValues!.Should().Contain("\"r-etag\"");
    }

    [Fact]
    public async Task FetchUnknownSystemAsync_SystemFound_UpsertsAndReturnsSystem()
    {
        var (svc, handler, systems, _, _) = Build();
        handler.EnqueueJson(new SolarSystem(30000142, "Jita", 20000020, 0.945f));

        var result = await svc.FetchUnknownSystemAsync(30000142, CancellationToken.None);

        result!.Name.Should().Be("Jita");
        systems.GetById(30000142)!.Name.Should().Be("Jita");
    }

    [Fact]
    public async Task FetchUnknownSystemAsync_404Response_ReturnsNullAndDoesNotStore()
    {
        var (svc, handler, systems, _, _) = Build();
        handler.EnqueueStatus(HttpStatusCode.NotFound);

        var result = await svc.FetchUnknownSystemAsync(99999999, CancellationToken.None);

        result.Should().BeNull();
        systems.GetById(99999999).Should().BeNull();
    }

    [Fact]
    public void GetSystem_Known_ReturnsSystem_Unknown_ReturnsNull()
    {
        var (svc, _, systems, _, _) = Build();
        var jita = new SolarSystem(30000142, "Jita", 20000020, 0.945f);
        systems.Upsert(jita);

        svc.GetSystem(30000142).Should().Be(jita);
        svc.GetSystem(99999999).Should().BeNull();
    }

    [Fact]
    public async Task RunPeriodicRefreshAsync_CancellationRequested_ExitsGracefully()
    {
        var (svc, _, _, _, _) = Build();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        // Should exit cleanly when the long delay is cancelled.
        Func<Task> act = () => svc.RunPeriodicRefreshAsync(cts.Token);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task InitializeAsync_ConcurrentFetchLimitedTo5()
    {
        // Enqueue a list with 20 IDs; all details succeed. We can't directly observe
        // concurrency level from outside, but we can at least verify all 20 are fetched
        // without deadlock or unordered IDs dropping.
        var (svc, handler, systems, _, _) = Build();

        var ids = Enumerable.Range(30000001, 20).ToArray();
        handler.EnqueueJson(Array.Empty<int>());        // regions
        handler.EnqueueJson(Array.Empty<int>());        // constellations
        handler.EnqueueJson(ids);                        // systems list
        foreach (var id in ids)
            handler.EnqueueJson(new SolarSystem(id, $"Sys-{id}", 0, 0));

        await svc.InitializeAsync(CancellationToken.None);

        systems.GetAll().Should().HaveCount(20);
        handler.RequestCount.Should().Be(3 + 20);
    }
}
