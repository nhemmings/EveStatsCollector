using EveStatsCollector.Models;

namespace EveStatsCollector.Repositories.InMemory;

public sealed class InMemoryConstellationRepository
    : InMemoryRepositoryBase<Constellation, int>, IConstellationRepository
{
    public InMemoryConstellationRepository() : base(c => c.ConstellationId) { }
}
