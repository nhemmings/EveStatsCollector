using System.Collections.Concurrent;
using EveStatsCollector.Models;

namespace EveStatsCollector.Repositories.InMemory;

public sealed class InMemoryJumpsReportRepository : IJumpsReportRepository
{
    private readonly ConcurrentDictionary<int, JumpsReport> _reports = new();
    private int _nextId;
    private volatile int _latestId;

    public Task<JumpsReport> AddAsync(DateTimeOffset lastModified, IReadOnlyList<SystemJumps> entries)
    {
        var id = Interlocked.Increment(ref _nextId);
        var report = new JumpsReport(id, lastModified, DateTimeOffset.UtcNow, entries);
        _reports[id] = report;
        _latestId = id;
        return Task.FromResult(report);
    }

    public Task<JumpsReport?> GetByIdAsync(int id) =>
        Task.FromResult(_reports.TryGetValue(id, out var report) ? report : null);

    public Task<JumpsReport?> GetLatestAsync() =>
        Task.FromResult(_latestId > 0 ? _reports.GetValueOrDefault(_latestId) : null);

    public Task<IReadOnlyList<JumpsReport>> GetAllAsync() =>
        Task.FromResult<IReadOnlyList<JumpsReport>>(_reports.Values.OrderBy(r => r.Id).ToList());
}
