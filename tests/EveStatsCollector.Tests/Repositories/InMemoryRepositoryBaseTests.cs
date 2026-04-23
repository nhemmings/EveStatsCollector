using EveStatsCollector.Models;
using EveStatsCollector.Repositories.InMemory;
using FluentAssertions;

namespace EveStatsCollector.Tests.Repositories;

/// <summary>
/// Exercises the generic InMemoryRepositoryBase via the three concrete entity
/// repositories (regions, constellations, systems).
/// </summary>
public class InMemoryRepositoryBaseTests
{
    [Fact]
    public void GetById_Empty_ReturnsNull()
    {
        var repo = new InMemorySolarSystemRepository();
        repo.GetById(30000142).Should().BeNull();
    }

    [Fact]
    public void Upsert_NewEntity_Stored()
    {
        var repo = new InMemorySolarSystemRepository();
        var jita = new SolarSystem(30000142, "Jita", 20000020, 0.945f);
        repo.Upsert(jita);

        repo.GetById(30000142).Should().Be(jita);
    }

    [Fact]
    public void Upsert_ExistingEntity_Overwrites()
    {
        var repo = new InMemorySolarSystemRepository();
        var v1 = new SolarSystem(30000142, "Jita", 20000020, 0.945f);
        var v2 = new SolarSystem(30000142, "Jita (updated)", 20000020, 0.9f);
        repo.Upsert(v1);
        repo.Upsert(v2);

        repo.GetById(30000142)!.Name.Should().Be("Jita (updated)");
    }

    [Fact]
    public void UpsertRange_StoresAllEntities()
    {
        var repo = new InMemorySolarSystemRepository();
        repo.UpsertRange(new[]
        {
            new SolarSystem(30000142, "Jita", 20000020, 0.945f),
            new SolarSystem(30000144, "Perimeter", 20000020, 0.945f),
        });

        repo.GetAll().Should().HaveCount(2);
        repo.GetById(30000144)!.Name.Should().Be("Perimeter");
    }

    [Fact]
    public void GetAll_ReturnsAllEntities()
    {
        var repo = new InMemoryRegionRepository();
        repo.Upsert(new Region(10000001, "Derelik", Array.Empty<int>()));
        repo.Upsert(new Region(10000002, "The Forge", Array.Empty<int>()));

        var all = repo.GetAll();

        all.Should().HaveCount(2);
        all.Select(r => r.Name).Should().BeEquivalentTo(new[] { "Derelik", "The Forge" });
    }

    [Fact]
    public void ConstellationRepository_StoresAndRetrievesById()
    {
        var repo = new InMemoryConstellationRepository();
        var cons = new Constellation(20000001, "San Matar", 10000001, new[] { 30000001 });
        repo.Upsert(cons);

        repo.GetById(20000001).Should().Be(cons);
        repo.GetAll().Should().ContainSingle();
    }

    [Fact]
    public async Task Upsert_ConcurrentWriters_AllEntriesVisible()
    {
        var repo = new InMemorySolarSystemRepository();
        var tasks = Enumerable.Range(1, 500).Select(id => Task.Run(() =>
            repo.Upsert(new SolarSystem(id, $"Sys-{id}", 0, 0)))).ToArray();

        await Task.WhenAll(tasks);
        repo.GetAll().Should().HaveCount(500);
    }
}
