using EveStatsCollector.Models;

namespace EveStatsCollector.Repositories;

public sealed class FilteredKillsReportRepository : IKillsReportRepository
{
    private readonly IKillsReportRepository _inner;
    private readonly ConstellationFilter _filter;

    public FilteredKillsReportRepository(IKillsReportRepository inner, ConstellationFilter filter)
    {
        _inner = inner;
        _filter = filter;
    }

    public KillsReport Add(DateTimeOffset lastModified, IReadOnlyList<SystemKills> entries) =>
        _inner.Add(lastModified, entries.Where(e => _filter.AllowSystem(e.SystemId)).ToList());

    public KillsReport? GetById(int id) => _inner.GetById(id);
    public KillsReport? GetLatest() => _inner.GetLatest();
    public IReadOnlyList<KillsReport> GetAll() => _inner.GetAll();
}
