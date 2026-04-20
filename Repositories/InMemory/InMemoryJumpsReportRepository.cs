using System.Collections.Concurrent;
using EveStatsCollector.Models;

namespace EveStatsCollector.Repositories.InMemory;

public sealed class InMemoryJumpsReportRepository : IJumpsReportRepository
{
    private readonly ConcurrentDictionary<int, JumpsReport> _reports = new();
    private int _nextId;
    private volatile int _latestId;

    public JumpsReport Add(DateTimeOffset lastModified, IReadOnlyList<SystemJumps> entries)
    {
        var id = Interlocked.Increment(ref _nextId);
        var report = new JumpsReport(id, lastModified, DateTimeOffset.UtcNow, entries);
        _reports[id] = report;
        _latestId = id;
        return report;
    }

    public JumpsReport? GetById(int id) =>
        _reports.TryGetValue(id, out var report) ? report : null;

    public JumpsReport? GetLatest() =>
        _latestId > 0 ? _reports.GetValueOrDefault(_latestId) : null;

    public IReadOnlyList<JumpsReport> GetAll() =>
        _reports.Values.OrderBy(r => r.Id).ToList();
}
