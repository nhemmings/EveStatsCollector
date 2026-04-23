using EveStatsCollector.Models;
using EveStatsCollector.Repositories.InMemory;
using FluentAssertions;

namespace EveStatsCollector.Tests.Repositories;

public class InMemoryJumpsReportRepositoryTests
{
    [Fact]
    public async Task Add_AssignsSequentialIds()
    {
        var repo = new InMemoryJumpsReportRepository();
        var r1 = await repo.AddAsync(DateTimeOffset.UtcNow, Array.Empty<SystemJumps>());
        var r2 = await repo.AddAsync(DateTimeOffset.UtcNow, Array.Empty<SystemJumps>());
        r1.Id.Should().Be(1);
        r2.Id.Should().Be(2);
    }

    [Fact]
    public async Task Add_PopulatesEntriesAndTimestamps()
    {
        var repo = new InMemoryJumpsReportRepository();
        var ts = DateTimeOffset.Parse("2026-04-21T10:00:00Z");
        var entries = new[] { new SystemJumps(30000142, 123) };

        var report = await repo.AddAsync(ts, entries);

        report.Entries.Should().BeEquivalentTo(entries);
        report.LastModified.Should().Be(ts);
    }

    [Fact]
    public async Task GetById_ReturnsStoredReport()
    {
        var repo = new InMemoryJumpsReportRepository();
        var added = await repo.AddAsync(DateTimeOffset.UtcNow, Array.Empty<SystemJumps>());
        (await repo.GetByIdAsync(added.Id)).Should().Be(added);
        (await repo.GetByIdAsync(9999)).Should().BeNull();
    }

    [Fact]
    public async Task GetLatest_ReturnsMostRecentReport()
    {
        var repo = new InMemoryJumpsReportRepository();
        (await repo.GetLatestAsync()).Should().BeNull();
        await repo.AddAsync(DateTimeOffset.UtcNow, Array.Empty<SystemJumps>());
        var second = await repo.AddAsync(DateTimeOffset.UtcNow, Array.Empty<SystemJumps>());
        (await repo.GetLatestAsync())!.Id.Should().Be(second.Id);
    }

    [Fact]
    public async Task GetAll_ReturnsOrderedById()
    {
        var repo = new InMemoryJumpsReportRepository();
        await repo.AddAsync(DateTimeOffset.UtcNow, Array.Empty<SystemJumps>());
        await repo.AddAsync(DateTimeOffset.UtcNow, Array.Empty<SystemJumps>());
        (await repo.GetAllAsync()).Select(r => r.Id).Should().ContainInOrder(1, 2);
    }

    [Fact]
    public async Task Add_ConcurrentAdds_IdsAreUnique()
    {
        var repo = new InMemoryJumpsReportRepository();
        var tasks = Enumerable.Range(0, 100).Select(_ => Task.Run(() =>
            repo.AddAsync(DateTimeOffset.UtcNow, Array.Empty<SystemJumps>()))).ToArray();
        var reports = await Task.WhenAll(tasks);
        reports.Select(r => r.Id).Distinct().Count().Should().Be(100);
    }
}
