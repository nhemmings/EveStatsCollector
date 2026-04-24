using EveStatsCollector.Models;
using EveStatsCollector.Repositories;
using Microsoft.Extensions.Logging;

namespace EveStatsCollector.Diagnostics;

internal static class ReportDebugLogger
{
    internal static void LogKillsReport(
        ILogger logger,
        KillsReport report,
        Func<int, string?> resolveName,
        ReportConstellationFilter filter)
    {
        logger.LogDebug("Kills report #{Id} | Last-Modified: {LastModified:u}", report.Id, report.LastModified);
        foreach (var e in report.Entries.Where(e => filter.AllowSystem(e.SystemId)))
            logger.LogDebug("  {SystemName}: {ShipKills} ship / {NpcKills} NPC / {PodKills} pod",
                resolveName(e.SystemId) ?? e.SystemId.ToString(), e.ShipKills, e.NpcKills, e.PodKills);
    }

    internal static void LogJumpsReport(
        ILogger logger,
        JumpsReport report,
        Func<int, string?> resolveName,
        ReportConstellationFilter filter)
    {
        logger.LogDebug("Jumps report #{Id} | Last-Modified: {LastModified:u}", report.Id, report.LastModified);
        foreach (var e in report.Entries.Where(e => filter.AllowSystem(e.SystemId)))
            logger.LogDebug("  {SystemName}: {ShipJumps} jumps",
                resolveName(e.SystemId) ?? e.SystemId.ToString(), e.ShipJumps);
    }
}
