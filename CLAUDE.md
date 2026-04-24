# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
dotnet build                                          # build all projects
dotnet run                                            # run the main application
dotnet test tests/EveStatsCollector.Tests             # run all tests
dotnet test tests/EveStatsCollector.Tests --filter "FullyQualifiedName~ClassName"  # run a single test class
dotnet run --project EveStatsCollector.Migrator       # run DB migrations
dotnet publish                                        # publish for deployment
```

Tests use xUnit + FluentAssertions + Moq.

## Architecture

**EveStatsCollector** is a .NET 10 console application that continuously polls the [ESI (Eve Swagger Interface) API](https://esi.evetech.net/latest/) for kill and jump statistics across Eve Online's ~8,000 solar systems.

### Startup sequence (`Program.cs`)

1. Configures Serilog (Console + Seq at `http://localhost:5341`)
2. Registers DI: `EsiRateLimiter`, `EsiErrorTracker`, `EsiRateLimitHandler`, `EsiClient`, repositories, `UniverseService`, `StatsService`
3. If PostgreSQL is configured, calls `LoadAsync` on all `IAsyncPreloadable` services (the three universe repos) to pre-populate their caches from the DB
4. Calls `UniverseService.InitializeAsync()` — fetches from ESI only the entities missing from the cache, then persists any new ones back to Postgres
5. Runs `UniverseService.RunPeriodicRefreshAsync()` and `StatsService.RunAsync()` concurrently

### ESI layer (`Esi/`)

- **`EsiClient`** — thin HTTP wrapper; handles ETags (304 Not Modified → return cached value), retries on HTTP 429 (up to 3×), parses `X-Esi-Error-Limit-*` headers
- **`EsiRateLimitHandler`** — `DelegatingHandler` that calls `EsiRateLimiter.ThrottleAsync` before each request and updates limiter state from response headers; registered on the named `"ESI"` `HttpClient`
- **`EsiRateLimiter`** — floating-window rate limiter keyed by ESI rate-limit group; computes graduated delays (0 ms → 10 s) based on remaining tokens (>50% = 0 ms, 0% = 10 s+)
- **`EsiErrorTracker`** — tracks legacy per-minute error budget (100 errors/min) for older ESI routes

### Services (`Services/`)

- **`UniverseService`** — loads all regions/constellations/systems into repositories on init; refreshes every 24 h ±30 min jitter; lazily fetches details for systems that appear in live data but weren't known at startup; max concurrency of 5 for parallel system fetches
- **`StatsService`** — polls `/universe/system_kills/` and `/universe/system_jumps/`; stores timestamped `KillsReport`/`JumpsReport` snapshots; schedules next fetch based on the response `Expires` header; detects unknown systems and triggers `UniverseService` to fill them in

### Repositories (`Repositories/`)

Base class `InMemoryRepositoryBase<T, TKey>` uses `ConcurrentDictionary`. All five repositories have two implementations selected at startup based on whether `ConnectionStrings:Postgres` is set:

- **In-memory** (default) — universe repos use `InMemoryRepositoryBase`; report repos auto-increment IDs and expose `GetLatest()`; all data lost on shutdown
- **PostgreSQL** — universe repos (`PostgreSqlRegionRepository`, `PostgreSqlConstellationRepository`, `PostgreSqlSolarSystemRepository`) keep an in-memory cache for reads and write-through to Postgres on every `Upsert`; they also implement `IAsyncPreloadable` so they can be pre-populated from DB on startup. Report repos persist `kills_reports`/`kills_entries` and `jumps_reports`/`jumps_entries`.

The `IRepository<T, TKey>` interface is synchronous. PostgreSQL universe repos use `.GetAwaiter().GetResult()` on write paths — this is safe in the .NET Generic Host (no `SynchronizationContext`), and write concurrency is already capped at 5 by the semaphore in `UniverseService`.

### EveStatsCollector.Migrator

Separate console project using DbUp. Runs SQL migration scripts embedded in the assembly (`Migrations/*.sql`). Reads the connection string from `ConnectionStrings__Postgres` env var or the first CLI argument.

### Configuration (`appsettings.json`)

| Key | Purpose |
|-----|---------|
| `Esi:BaseUrl` | ESI API root (default: `https://esi.evetech.net/latest/`) |
| `Esi:UserAgent` | `User-Agent` header sent with every ESI request |
| `Seq:ServerUrl` | Seq structured-logging server (default: `http://localhost:5341`) |
| `ConnectionStrings:Postgres` | If set, switches report storage to PostgreSQL |
| `Filter:Constellations` | Optional string array of constellation names; when set, stats are filtered to only those systems. `"all"` entries are ignored. |
