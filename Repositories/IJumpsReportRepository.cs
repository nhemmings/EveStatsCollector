using EveStatsCollector.Models;

namespace EveStatsCollector.Repositories;

public interface IJumpsReportRepository
{
    Task<JumpsReport> AddAsync(DateTimeOffset lastModified, IReadOnlyList<SystemJumps> entries);
    Task<JumpsReport?> GetByIdAsync(int id);
    Task<JumpsReport?> GetLatestAsync();
    Task<IReadOnlyList<JumpsReport>> GetAllAsync();
}
