using System.Collections.Concurrent;
using EveStatsCollector.Esi;
using EveStatsCollector.Models;
using EveStatsCollector.Repositories;
using Microsoft.Extensions.Logging;

namespace EveStatsCollector.Services;

public sealed class UniverseService
{
    private readonly EsiClient _client;
    private readonly ISolarSystemRepository _systems;
    private readonly IConstellationRepository _constellations;
    private readonly IRegionRepository _regions;
    private readonly UniverseConstellationFilter _universeFilter;
    private readonly ILogger<UniverseService> _logger;

    private string? _systemIdsETag;
    private string? _constellationIdsETag;
    private string? _regionIdsETag;

    private const int MaxConcurrency = 5;
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(24);

    public bool IsFilteredLoadActive => _universeFilter.IsActive;

    public UniverseService(
        EsiClient client,
        ISolarSystemRepository systems,
        IConstellationRepository constellations,
        IRegionRepository regions,
        UniverseConstellationFilter universeFilter,
        ILogger<UniverseService> logger)
    {
        _client = client;
        _systems = systems;
        _constellations = constellations;
        _regions = regions;
        _universeFilter = universeFilter;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        if (_universeFilter.IsActive)
            _logger.LogInformation("Loading filtered universe data (constellation filter active)");
        else
            _logger.LogInformation(
                "Loading full universe data from ESI. " +
                "Regions and constellations load quickly; solar systems (~8000 endpoints) may take several minutes.");

        await LoadAllAsync(ct);

        _logger.LogInformation(
            "Universe data ready: {SystemCount} systems, {ConstellationCount} constellations, {RegionCount} regions",
            _systems.GetAll().Count, _constellations.GetAll().Count, _regions.GetAll().Count);
    }

    public async Task RunPeriodicRefreshAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var jitter = TimeSpan.FromMinutes(Random.Shared.Next(-30, 31));
            var delay = RefreshInterval + jitter;
            _logger.LogInformation("Next universe refresh in {Hours:F1} hours", delay.TotalHours);

            try { await Task.Delay(delay, ct); }
            catch (OperationCanceledException) { break; }

            _logger.LogInformation("Starting periodic universe refresh");
            try { await LoadAllAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Periodic universe refresh failed"); }
        }
    }

    public SolarSystem? GetSystem(int systemId) => _systems.GetById(systemId);

    // Called by StatsService when a kill/jump references a system not found at startup.
    public async Task<SolarSystem?> FetchUnknownSystemAsync(int systemId, CancellationToken ct)
    {
        var response = await _client.GetAsync<SolarSystem>($"universe/systems/{systemId}/", ct: ct);
        if (response.Data is not null)
            _systems.Upsert(response.Data);
        return response.Data;
    }

    private Task LoadAllAsync(CancellationToken ct) =>
        _universeFilter.IsActive ? LoadFilteredAsync(ct) : LoadUnfilteredAsync(ct);

    private async Task LoadUnfilteredAsync(CancellationToken ct)
    {
        _regionIdsETag = await LoadEntityTypeAsync<Region>(
            "universe/regions/", "universe/regions/{0}/", _regionIdsETag, _regions, ct);

        _constellationIdsETag = await LoadEntityTypeAsync<Constellation>(
            "universe/constellations/", "universe/constellations/{0}/", _constellationIdsETag, _constellations, ct);

        _systemIdsETag = await LoadEntityTypeAsync<SolarSystem>(
            "universe/systems/", "universe/systems/{0}/", _systemIdsETag, _systems, ct);
    }

    private async Task LoadFilteredAsync(CancellationToken ct)
    {
        var (allIds, newETag) = await FetchIdsAsync("universe/constellations/", _constellationIdsETag, ct);
        if (allIds is null)
        {
            _logger.LogDebug("universe/constellations/ unchanged (304)");
            return;
        }
        _constellationIdsETag = newETag;

        // Phase 1: fetch details for any constellation IDs not yet in the repo and store matching ones.
        var missingIds = allIds.Where(id => _constellations.GetById(id) is null).ToList();

        if (missingIds.Count > 0)
        {
            _logger.LogInformation(
                "Universe filter: checking {Missing} of {Total} constellation(s) to resolve names",
                missingIds.Count, allIds.Length);

            var semaphore = new SemaphoreSlim(MaxConcurrency);
            await Task.WhenAll(missingIds.Select(async id =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    var r = await _client.GetAsync<Constellation>($"universe/constellations/{id}/", ct: ct);
                    if (r.Data is not null && _universeFilter.AllowConstellation(r.Data.Name))
                        _constellations.Upsert(r.Data);
                }
                finally { semaphore.Release(); }
            }));
        }

        // Phase 2: load regions and systems for all constellations currently in the repo that match the filter.
        var matched = _constellations.GetAll()
            .Where(c => _universeFilter.AllowConstellation(c.Name))
            .ToList();

        if (matched.Count == 0)
        {
            _logger.LogWarning("Universe filter is active but no matching constellations were found");
            return;
        }

        _logger.LogInformation(
            "Universe filter: {Count} matching constellation(s), loading regions and systems",
            matched.Count);

        var semaphore2 = new SemaphoreSlim(MaxConcurrency);

        var missingRegionIds = matched
            .Select(c => c.RegionId)
            .Distinct()
            .Where(id => _regions.GetById(id) is null)
            .ToList();

        await Task.WhenAll(missingRegionIds.Select(async id =>
        {
            await semaphore2.WaitAsync(ct);
            try
            {
                var r = await _client.GetAsync<Region>($"universe/regions/{id}/", ct: ct);
                if (r.Data is not null)
                    _regions.Upsert(r.Data);
            }
            finally { semaphore2.Release(); }
        }));

        var missingSystemIds = matched
            .SelectMany(c => c.Systems)
            .Distinct()
            .Where(id => _systems.GetById(id) is null)
            .ToList();

        int loaded = 0;
        await Task.WhenAll(missingSystemIds.Select(async id =>
        {
            await semaphore2.WaitAsync(ct);
            try
            {
                var r = await _client.GetAsync<SolarSystem>($"universe/systems/{id}/", ct: ct);
                if (r.Data is not null)
                {
                    _systems.Upsert(r.Data);
                    Interlocked.Increment(ref loaded);
                }
            }
            finally { semaphore2.Release(); }
        }));

        _logger.LogInformation(
            "Universe filter: loaded {SystemCount} system(s) across {ConstellationCount} constellation(s)",
            loaded, matched.Count);
    }

    // Returns the new ETag for the ID list endpoint.
    private async Task<string?> LoadEntityTypeAsync<T>(
        string listPath,
        string detailPathTemplate,
        string? currentETag,
        IRepository<T, int> repo,
        CancellationToken ct)
    {
        var (ids, newETag) = await FetchIdsAsync(listPath, currentETag, ct);

        if (ids is null)
        {
            _logger.LogDebug("{ListPath} ID list unchanged (304)", listPath);
            return currentETag;
        }

        var missing = ids.Where(id => repo.GetById(id) is null).ToList();

        if (missing.Count == 0)
        {
            _logger.LogDebug("{ListPath}: {Total} IDs, all already loaded", listPath, ids.Length);
            return newETag;
        }

        _logger.LogInformation(
            "{ListPath}: {Missing} of {Total} entries not yet loaded — fetching details",
            listPath, missing.Count, ids.Length);

        var semaphore = new SemaphoreSlim(MaxConcurrency);
        int loaded = 0;

        var tasks = missing.Select(async id =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var path = string.Format(detailPathTemplate, id);
                var response = await _client.GetAsync<T>(path, ct: ct);
                if (response.Data is not null)
                {
                    repo.Upsert(response.Data);
                    Interlocked.Increment(ref loaded);
                }
            }
            finally { semaphore.Release(); }
        });

        await Task.WhenAll(tasks);

        _logger.LogInformation("{ListPath}: loaded {Loaded} details", listPath, loaded);
        return newETag;
    }

    // Null return means 304 — caller should keep using the current ETag.
    private async Task<(int[]? Ids, string? ETag)> FetchIdsAsync(string path, string? etag, CancellationToken ct)
    {
        var response = await _client.GetAsync<int[]>(path, etag, ct: ct);

        if (response.IsNotModified)
            return (null, etag);

        return (response.Data ?? Array.Empty<int>(), response.ETag);
    }
}
