using EveStatsCollector.Models;

namespace EveStatsCollector.Repositories;

public interface IKillsReportRepository
{
    KillsReport Add(DateTimeOffset lastModified, IReadOnlyList<SystemKills> entries);
    KillsReport? GetById(int id);
    KillsReport? GetLatest();
    IReadOnlyList<KillsReport> GetAll();
}
