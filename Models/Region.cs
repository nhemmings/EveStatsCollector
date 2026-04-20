namespace EveStatsCollector.Models;

public record Region(
    int RegionId,
    string Name,
    int[] Constellations
);
