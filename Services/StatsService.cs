using EveStatsCollector.Diagnostics;
using EveStatsCollector.Esi;
using EveStatsCollector.Models;
using EveStatsCollector.Repositories;
using Microsoft.Extensions.Logging;

namespace EveStatsCollector.Services;

public sealed class StatsService
{
    private readonly EsiClient _client;
    private readonly UniverseService _universe;
    private readonly IKillsReportRepository _killsReports;
    private readonly IJumpsReportRepository _jumpsReports;
    private readonly ILogger<StatsService> _logger;

    private string? _killsETag;
    private string? _jumpsETag;

    public StatsService(
        EsiClient client,
        UniverseService universe,
        IKillsReportRepository killsReports,
        IJumpsReportRepository jumpsReports,
        ILogger<StatsService> logger)
    {
        _client = client;
        _universe = universe;
        _killsReports = killsReports;
        _jumpsReports = jumpsReports;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _logger.LogInformation("StatsService starting");

        while (!ct.IsCancellationRequested)
        {
            DateTimeOffset? expires = null;

            try
            {
                expires = await FetchAndStoreAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during stats fetch — will retry after backoff");
            }

            var sleepUntil = CalculateNextFetch(expires);
            var delay = sleepUntil - DateTimeOffset.UtcNow;

            if (delay > TimeSpan.Zero)
            {
                _logger.LogInformation("Next fetch at {SleepUntil:u} (in {Minutes:F1} min)", sleepUntil, delay.TotalMinutes);
                try { await Task.Delay(delay, ct); }
                catch (OperationCanceledException) { break; }
            }
        }

        _logger.LogInformation("StatsService stopped");
    }

    private async Task<DateTimeOffset?> FetchAndStoreAsync(CancellationToken ct)
    {
        _logger.LogInformation("Fetching system_kills and system_jumps");

        var killsResponse = await _client.GetAsync<SystemKills[]>("universe/system_kills/", _killsETag, ct: ct);
        var jumpsResponse = await _client.GetAsync<SystemJumps[]>("universe/system_jumps/", _jumpsETag, ct: ct);

        if (killsResponse.IsSuccess && killsResponse.Data is not null)
        {
            _killsETag = killsResponse.ETag;
            var lastModified = killsResponse.LastModified ?? killsResponse.Expires ?? DateTimeOffset.UtcNow;
            var report = _killsReports.Add(lastModified, killsResponse.Data);
            _logger.LogInformation(
                "Kills report #{Id} stored: {Count} systems, Last-Modified {LastModified:u}",
                report.Id, report.Entries.Count, report.LastModified);
            ReportDebugLogger.LogKillsReport(_logger, report, id => _universe.GetSystem(id)?.Name);
        }
        else if (killsResponse.IsNotModified)
        {
            _logger.LogDebug("Kills unchanged (304) — no new report created");
        }

        if (jumpsResponse.IsSuccess && jumpsResponse.Data is not null)
        {
            _jumpsETag = jumpsResponse.ETag;
            var lastModified = jumpsResponse.LastModified ?? jumpsResponse.Expires ?? DateTimeOffset.UtcNow;
            var report = _jumpsReports.Add(lastModified, jumpsResponse.Data);
            _logger.LogInformation(
                "Jumps report #{Id} stored: {Count} systems, Last-Modified {LastModified:u}",
                report.Id, report.Entries.Count, report.LastModified);
            ReportDebugLogger.LogJumpsReport(_logger, report, id => _universe.GetSystem(id)?.Name);
        }
        else if (jumpsResponse.IsNotModified)
        {
            _logger.LogDebug("Jumps unchanged (304) — no new report created");
        }

        await CheckForUnknownSystemsAsync(killsResponse.Data, jumpsResponse.Data, ct);

        return killsResponse.Expires.HasValue && jumpsResponse.Expires.HasValue
            ? killsResponse.Expires < jumpsResponse.Expires ? killsResponse.Expires : jumpsResponse.Expires
            : killsResponse.Expires ?? jumpsResponse.Expires;
    }

    private async Task CheckForUnknownSystemsAsync(
        SystemKills[]? kills,
        SystemJumps[]? jumps,
        CancellationToken ct)
    {
        var ids = (kills?.Select(k => k.SystemId) ?? [])
            .Concat(jumps?.Select(j => j.SystemId) ?? [])
            .Distinct()
            .Where(id => _universe.GetSystem(id) is null);

        foreach (var id in ids)
        {
            var system = await _universe.FetchUnknownSystemAsync(id, ct);
            _logger.LogWarning(
                "Kill/jump data references system not present at startup: {SystemId} ({SystemName})",
                id, system?.Name ?? "could not resolve");
        }
    }

    private static DateTimeOffset CalculateNextFetch(DateTimeOffset? expires)
    {
        var baseTime = expires ?? DateTimeOffset.UtcNow.AddHours(1);
        var jitter = TimeSpan.FromSeconds(Random.Shared.Next(-30, 31));
        return baseTime + jitter;
    }
}
