using EveStatsCollector.Models;

namespace EveStatsCollector.Repositories;

public interface IKillsReportRepository
{
    Task<KillsReport> AddAsync(DateTimeOffset lastModified, IReadOnlyList<SystemKills> entries);
    Task<KillsReport?> GetByIdAsync(int id);
    Task<KillsReport?> GetLatestAsync();
    Task<IReadOnlyList<KillsReport>> GetAllAsync();
}
