namespace EveStatsCollector.Models;

public record SolarSystem(
    int SystemId,
    string Name,
    int ConstellationId,
    float SecurityStatus
);
