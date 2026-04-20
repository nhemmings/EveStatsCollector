using EveStatsCollector.Models;

namespace EveStatsCollector.Repositories;

public interface IJumpsReportRepository
{
    JumpsReport Add(DateTimeOffset lastModified, IReadOnlyList<SystemJumps> entries);
    JumpsReport? GetById(int id);
    JumpsReport? GetLatest();
    IReadOnlyList<JumpsReport> GetAll();
}
