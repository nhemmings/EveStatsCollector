using EveStatsCollector.Models;
using EveStatsCollector.Repositories;
using EveStatsCollector.Repositories.InMemory;
using FluentAssertions;

namespace EveStatsCollector.Tests.Repositories;

public class ConstellationFilterTests
{
    private static InMemoryConstellationRepository BuildRepo()
    {
        var repo = new InMemoryConstellationRepository();
        repo.Upsert(new Constellation(20000001, "San Matar", 10000001, new[] { 30000001, 30000002 }));
        repo.Upsert(new Constellation(20000002, "Kimotoro", 10000002, new[] { 30000142, 30000144 }));
        return repo;
    }

    [Fact]
    public void IsActive_EmptyNames_ReturnsFalse()
    {
        var filter = new ConstellationFilter(new InMemoryConstellationRepository(), Array.Empty<string>());
        filter.IsActive.Should().BeFalse();
    }

    [Fact]
    public void IsActive_NamesProvided_ReturnsTrue()
    {
        var filter = new ConstellationFilter(BuildRepo(), new[] { "Kimotoro" });
        filter.IsActive.Should().BeTrue();
    }

    [Fact]
    public void AllowSystem_FilterInactive_AllSystemsAllowed()
    {
        var filter = new ConstellationFilter(BuildRepo(), Array.Empty<string>());
        filter.AllowSystem(30000142).Should().BeTrue();
        filter.AllowSystem(99999999).Should().BeTrue();
    }

    [Fact]
    public void AllowSystem_FilterActive_OnlyAllowsConfiguredSystems()
    {
        var filter = new ConstellationFilter(BuildRepo(), new[] { "Kimotoro" });
        filter.AllowSystem(30000142).Should().BeTrue();
        filter.AllowSystem(30000144).Should().BeTrue();
        filter.AllowSystem(30000001).Should().BeFalse();
    }

    [Fact]
    public void AllowSystem_FilterIsCaseInsensitive()
    {
        var filter = new ConstellationFilter(BuildRepo(), new[] { "kimotoro" });
        filter.AllowSystem(30000142).Should().BeTrue();
    }

    [Fact]
    public void AllowSystem_UnknownConstellationName_AllowsNothing()
    {
        var filter = new ConstellationFilter(BuildRepo(), new[] { "does-not-exist" });
        filter.AllowSystem(30000142).Should().BeFalse();
    }

}
