using EveStatsCollector.Models;

namespace EveStatsCollector.Repositories.InMemory;

public sealed class InMemorySolarSystemRepository
    : InMemoryRepositoryBase<SolarSystem, int>, ISolarSystemRepository
{
    public InMemorySolarSystemRepository() : base(s => s.SystemId) { }
}
