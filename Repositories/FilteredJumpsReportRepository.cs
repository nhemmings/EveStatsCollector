using EveStatsCollector.Models;

namespace EveStatsCollector.Repositories;

public sealed class FilteredJumpsReportRepository : IJumpsReportRepository
{
    private readonly IJumpsReportRepository _inner;
    private readonly ConstellationFilter _filter;

    public FilteredJumpsReportRepository(IJumpsReportRepository inner, ConstellationFilter filter)
    {
        _inner = inner;
        _filter = filter;
    }

    public JumpsReport Add(DateTimeOffset lastModified, IReadOnlyList<SystemJumps> entries) =>
        _inner.Add(lastModified, entries.Where(e => _filter.AllowSystem(e.SystemId)).ToList());

    public JumpsReport? GetById(int id) => _inner.GetById(id);
    public JumpsReport? GetLatest() => _inner.GetLatest();
    public IReadOnlyList<JumpsReport> GetAll() => _inner.GetAll();
}
