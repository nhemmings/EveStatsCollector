using EveStatsCollector.Models;

namespace EveStatsCollector.Repositories.InMemory;

public sealed class InMemoryRegionRepository
    : InMemoryRepositoryBase<Region, int>, IRegionRepository
{
    public InMemoryRegionRepository() : base(r => r.RegionId) { }
}
