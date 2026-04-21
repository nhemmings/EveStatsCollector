# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
dotnet build          # build the project
dotnet run            # run the application
dotnet publish        # publish for deployment
```

There are no automated tests or linting configuration.

## Architecture

**EveStatsCollector** is a .NET 10 console application that continuously polls the [ESI (Eve Swagger Interface) API](https://esi.evetech.net/latest/) for kill and jump statistics across Eve Online's ~8,000 solar systems, keeping an in-memory archive of historical reports.

### Startup sequence (`Program.cs`)

1. Configures Serilog (Console + Seq at `http://localhost:5341`)
2. Registers DI: `EsiRateLimiter`, `EsiErrorTracker`, `EsiClient`, repositories, `UniverseService`, `StatsService`
3. Calls `UniverseService.InitializeAsync()` — fetches the full universe (regions → constellations → systems) before anything else
4. Runs `UniverseService.RunPeriodicRefreshAsync()` and `StatsService.RunAsync()` concurrently

### ESI layer (`Esi/`)

- **`EsiClient`** — thin HTTP wrapper; adds `User-Agent`, handles ETags (304 Not Modified → return cached value), retries on HTTP 429 (up to 3×), parses `X-Esi-Error-Limit-*` and per-group rate-limit headers from responses
- **`EsiRateLimiter`** — floating-window rate limiter keyed by ESI rate-limit group; computes graduated delays (0 ms → 10 s) based on remaining tokens
- **`EsiErrorTracker`** — tracks legacy per-minute error budget (100 errors/min) for older ESI routes

### Services (`Services/`)

- **`UniverseService`** — loads all regions/constellations/systems into repositories on init; refreshes every 24 h ±30 min jitter; lazily fetches details for systems that appear in live data but weren't known at startup; max concurrency of 5 for parallel system fetches
- **`StatsService`** — polls `/universe/system_kills/` and `/universe/system_jumps/`; stores timestamped `KillsReport`/`JumpsReport` snapshots; schedules next fetch based on the response `Expires` header; detects unknown systems in live data and triggers `UniverseService` to fill them in

### Repositories (`Repositories/`)

All storage is in-memory (lost on shutdown). Base class `InMemoryRepositoryBase<T, TKey>` uses `ConcurrentDictionary`. Report repositories (`InMemoryKillsReportRepository`, `InMemoryJumpsReportRepository`) auto-increment IDs and expose a `GetLatest()` method.

### Configuration (`appsettings.json`)

| Key | Purpose |
|-----|---------|
| `Esi:BaseUrl` | ESI API root (default: `https://esi.evetech.net/latest/`) |
| `Esi:UserAgent` | `User-Agent` header sent with every ESI request |
| `Seq:ServerUrl` | Seq structured-logging server (default: `http://localhost:5341`) |
