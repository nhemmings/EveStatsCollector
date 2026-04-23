using System.Collections.Concurrent;
using EveStatsCollector.Models;

namespace EveStatsCollector.Repositories.InMemory;

public sealed class InMemoryKillsReportRepository : IKillsReportRepository
{
    private readonly ConcurrentDictionary<int, KillsReport> _reports = new();
    private int _nextId;
    private volatile int _latestId;

    public Task<KillsReport> AddAsync(DateTimeOffset lastModified, IReadOnlyList<SystemKills> entries)
    {
        var id = Interlocked.Increment(ref _nextId);
        var report = new KillsReport(id, lastModified, DateTimeOffset.UtcNow, entries);
        _reports[id] = report;
        _latestId = id;
        return Task.FromResult(report);
    }

    public Task<KillsReport?> GetByIdAsync(int id) =>
        Task.FromResult(_reports.TryGetValue(id, out var report) ? report : null);

    public Task<KillsReport?> GetLatestAsync() =>
        Task.FromResult(_latestId > 0 ? _reports.GetValueOrDefault(_latestId) : null);

    public Task<IReadOnlyList<KillsReport>> GetAllAsync() =>
        Task.FromResult<IReadOnlyList<KillsReport>>(_reports.Values.OrderBy(r => r.Id).ToList());
}
