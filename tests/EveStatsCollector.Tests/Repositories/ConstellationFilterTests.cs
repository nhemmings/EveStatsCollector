using EveStatsCollector.Models;
using EveStatsCollector.Repositories;
using EveStatsCollector.Repositories.InMemory;
using FluentAssertions;

namespace EveStatsCollector.Tests.Repositories;

public class ReportConstellationFilterTests
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
        var filter = new ReportConstellationFilter(new InMemoryConstellationRepository(), Array.Empty<string>());
        filter.IsActive.Should().BeFalse();
    }

    [Fact]
    public void IsActive_NamesProvided_ReturnsTrue()
    {
        var filter = new ReportConstellationFilter(BuildRepo(), new[] { "Kimotoro" });
        filter.IsActive.Should().BeTrue();
    }

    [Fact]
    public void AllowSystem_FilterInactive_AllSystemsAllowed()
    {
        var filter = new ReportConstellationFilter(BuildRepo(), Array.Empty<string>());
        filter.AllowSystem(30000142).Should().BeTrue();
        filter.AllowSystem(99999999).Should().BeTrue();
    }

    [Fact]
    public void AllowSystem_FilterActive_OnlyAllowsConfiguredSystems()
    {
        var filter = new ReportConstellationFilter(BuildRepo(), new[] { "Kimotoro" });
        filter.AllowSystem(30000142).Should().BeTrue();
        filter.AllowSystem(30000144).Should().BeTrue();
        filter.AllowSystem(30000001).Should().BeFalse();
    }

    [Fact]
    public void AllowSystem_FilterIsCaseInsensitive()
    {
        var filter = new ReportConstellationFilter(BuildRepo(), new[] { "kimotoro" });
        filter.AllowSystem(30000142).Should().BeTrue();
    }

    [Fact]
    public void AllowSystem_UnknownConstellationName_AllowsNothing()
    {
        var filter = new ReportConstellationFilter(BuildRepo(), new[] { "does-not-exist" });
        filter.AllowSystem(30000142).Should().BeFalse();
    }
}

public class UniverseConstellationFilterTests
{
    [Fact]
    public void IsActive_EmptyNames_ReturnsFalse()
    {
        new UniverseConstellationFilter([]).IsActive.Should().BeFalse();
    }

    [Fact]
    public void IsActive_NamesProvided_ReturnsTrue()
    {
        new UniverseConstellationFilter(["Kimotoro"]).IsActive.Should().BeTrue();
    }

    [Fact]
    public void AllowConstellation_FilterInactive_AllAllowed()
    {
        var filter = new UniverseConstellationFilter([]);
        filter.AllowConstellation("Kimotoro").Should().BeTrue();
        filter.AllowConstellation("anything").Should().BeTrue();
    }

    [Fact]
    public void AllowConstellation_FilterActive_OnlyAllowsConfiguredNames()
    {
        var filter = new UniverseConstellationFilter(["Kimotoro"]);
        filter.AllowConstellation("Kimotoro").Should().BeTrue();
        filter.AllowConstellation("San Matar").Should().BeFalse();
    }

    [Fact]
    public void AllowConstellation_IsCaseInsensitive()
    {
        var filter = new UniverseConstellationFilter(["kimotoro"]);
        filter.AllowConstellation("Kimotoro").Should().BeTrue();
    }
}
