using System.Collections.Concurrent;
using EveStatsCollector.Models;
using Npgsql;

namespace EveStatsCollector.Repositories.PostgreSql;

internal sealed class PostgreSqlSolarSystemRepository : ISolarSystemRepository, IAsyncPreloadable
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ConcurrentDictionary<int, SolarSystem> _cache = new();

    public PostgreSqlSolarSystemRepository(NpgsqlDataSource dataSource) =>
        _dataSource = dataSource;

    public async Task LoadAsync(CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, constellation_id, security_status FROM solar_systems";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            _cache[reader.GetInt32(0)] = new SolarSystem(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetFloat(3));
    }

    public SolarSystem? GetById(int id) => _cache.TryGetValue(id, out var v) ? v : null;
    public IReadOnlyList<SolarSystem> GetAll() => _cache.Values.ToList();

    public void Upsert(SolarSystem entity)
    {
        _cache[entity.SystemId] = entity;
        WriteAsync(entity).GetAwaiter().GetResult();
    }

    public void UpsertRange(IEnumerable<SolarSystem> entities)
    {
        var list = entities.ToList();
        foreach (var e in list)
            _cache[e.SystemId] = e;
        WriteRangeAsync(list).GetAwaiter().GetResult();
    }

    private async Task WriteAsync(SolarSystem e)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO solar_systems (id, name, constellation_id, security_status)
            VALUES (@id, @name, @constellationId, @securityStatus)
            ON CONFLICT (id) DO UPDATE
                SET name             = EXCLUDED.name,
                    constellation_id = EXCLUDED.constellation_id,
                    security_status  = EXCLUDED.security_status
            """;
        cmd.Parameters.AddWithValue("id", e.SystemId);
        cmd.Parameters.AddWithValue("name", e.Name);
        cmd.Parameters.AddWithValue("constellationId", e.ConstellationId);
        cmd.Parameters.AddWithValue("securityStatus", e.SecurityStatus);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task WriteRangeAsync(IList<SolarSystem> entities)
    {
        if (entities.Count == 0) return;
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        foreach (var e in entities)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO solar_systems (id, name, constellation_id, security_status)
                VALUES (@id, @name, @constellationId, @securityStatus)
                ON CONFLICT (id) DO UPDATE
                    SET name             = EXCLUDED.name,
                        constellation_id = EXCLUDED.constellation_id,
                        security_status  = EXCLUDED.security_status
                """;
            cmd.Parameters.AddWithValue("id", e.SystemId);
            cmd.Parameters.AddWithValue("name", e.Name);
            cmd.Parameters.AddWithValue("constellationId", e.ConstellationId);
            cmd.Parameters.AddWithValue("securityStatus", e.SecurityStatus);
            await cmd.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
    }
}
