namespace EveStatsCollector.Models;

public record KillsReport(
    int Id,
    DateTimeOffset LastModified,
    DateTimeOffset FetchedAt,
    IReadOnlyList<SystemKills> Entries
);
