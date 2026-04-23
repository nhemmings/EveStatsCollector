using EveStatsCollector.Models;
using EveStatsCollector.Repositories.InMemory;
using FluentAssertions;

namespace EveStatsCollector.Tests.Repositories;

public class InMemoryKillsReportRepositoryTests
{
    [Fact]
    public async Task Add_AssignsSequentialIds()
    {
        var repo = new InMemoryKillsReportRepository();
        var r1 = await repo.AddAsync(DateTimeOffset.UtcNow, Array.Empty<SystemKills>());
        var r2 = await repo.AddAsync(DateTimeOffset.UtcNow, Array.Empty<SystemKills>());
        var r3 = await repo.AddAsync(DateTimeOffset.UtcNow, Array.Empty<SystemKills>());

        r1.Id.Should().Be(1);
        r2.Id.Should().Be(2);
        r3.Id.Should().Be(3);
    }

    [Fact]
    public async Task Add_PopulatesLastModifiedAndEntries()
    {
        var repo = new InMemoryKillsReportRepository();
        var ts = DateTimeOffset.Parse("2026-04-21T10:00:00Z");
        var entries = new[] { new SystemKills(30000142, 1, 2, 3) };

        var report = await repo.AddAsync(ts, entries);

        report.LastModified.Should().Be(ts);
        report.Entries.Should().BeEquivalentTo(entries);
        report.FetchedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GetById_ExistingId_ReturnsReport()
    {
        var repo = new InMemoryKillsReportRepository();
        var added = await repo.AddAsync(DateTimeOffset.UtcNow, Array.Empty<SystemKills>());
        (await repo.GetByIdAsync(added.Id)).Should().Be(added);
    }

    [Fact]
    public async Task GetById_UnknownId_ReturnsNull()
    {
        var repo = new InMemoryKillsReportRepository();
        (await repo.GetByIdAsync(999)).Should().BeNull();
    }

    [Fact]
    public async Task GetLatest_Empty_ReturnsNull()
    {
        var repo = new InMemoryKillsReportRepository();
        (await repo.GetLatestAsync()).Should().BeNull();
    }

    [Fact]
    public async Task GetLatest_MultipleReportsStored_ReturnsHighestId()
    {
        var repo = new InMemoryKillsReportRepository();
        await repo.AddAsync(DateTimeOffset.UtcNow, Array.Empty<SystemKills>());
        await repo.AddAsync(DateTimeOffset.UtcNow, Array.Empty<SystemKills>());
        var latest = await repo.AddAsync(DateTimeOffset.UtcNow, Array.Empty<SystemKills>());

        (await repo.GetLatestAsync())!.Id.Should().Be(latest.Id);
    }

    [Fact]
    public async Task GetAll_ReturnsReportsOrderedById()
    {
        var repo = new InMemoryKillsReportRepository();
        await repo.AddAsync(DateTimeOffset.UtcNow, Array.Empty<SystemKills>());
        await repo.AddAsync(DateTimeOffset.UtcNow, Array.Empty<SystemKills>());
        await repo.AddAsync(DateTimeOffset.UtcNow, Array.Empty<SystemKills>());

        (await repo.GetAllAsync()).Select(r => r.Id).Should().ContainInOrder(1, 2, 3);
    }

    [Fact]
    public async Task Add_ConcurrentAdds_AssignsUniqueIds()
    {
        var repo = new InMemoryKillsReportRepository();

        var tasks = Enumerable.Range(0, 200).Select(_ => Task.Run(() =>
            repo.AddAsync(DateTimeOffset.UtcNow, Array.Empty<SystemKills>()))).ToArray();

        var reports = await Task.WhenAll(tasks);
        reports.Select(r => r.Id).Distinct().Count().Should().Be(200);
        (await repo.GetAllAsync()).Should().HaveCount(200);
    }
}
