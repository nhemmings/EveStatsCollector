namespace EveStatsCollector.Models;

public record JumpsReport(
    int Id,
    DateTimeOffset LastModified,
    DateTimeOffset FetchedAt,
    IReadOnlyList<SystemJumps> Entries
);
