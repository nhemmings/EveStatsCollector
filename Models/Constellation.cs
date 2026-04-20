namespace EveStatsCollector.Models;

public record Constellation(
    int ConstellationId,
    string Name,
    int RegionId,
    int[] Systems
);
