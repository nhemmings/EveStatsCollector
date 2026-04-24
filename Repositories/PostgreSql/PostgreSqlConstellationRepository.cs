using System.Collections.Concurrent;
using EveStatsCollector.Models;
using Npgsql;

namespace EveStatsCollector.Repositories.PostgreSql;

internal sealed class PostgreSqlConstellationRepository : IConstellationRepository, IAsyncPreloadable
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ConcurrentDictionary<int, Constellation> _cache = new();

    public PostgreSqlConstellationRepository(NpgsqlDataSource dataSource) =>
        _dataSource = dataSource;

    public async Task LoadAsync(CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT c.id, c.name, c.region_id, ARRAY_REMOVE(ARRAY_AGG(s.id ORDER BY s.id), NULL)
            FROM constellations c
            LEFT JOIN solar_systems s ON s.constellation_id = c.id
            GROUP BY c.id, c.name, c.region_id
            """;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            _cache[reader.GetInt32(0)] = new Constellation(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetFieldValue<int[]>(3));
    }

    public Constellation? GetById(int id) => _cache.TryGetValue(id, out var v) ? v : null;
    public IReadOnlyList<Constellation> GetAll() => _cache.Values.ToList();

    public void Upsert(Constellation entity)
    {
        _cache[entity.ConstellationId] = entity;
        WriteAsync(entity).GetAwaiter().GetResult();
    }

    public void UpsertRange(IEnumerable<Constellation> entities)
    {
        var list = entities.ToList();
        foreach (var e in list)
            _cache[e.ConstellationId] = e;
        WriteRangeAsync(list).GetAwaiter().GetResult();
    }

    private async Task WriteAsync(Constellation e)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO constellations (id, name, region_id)
            VALUES (@id, @name, @regionId)
            ON CONFLICT (id) DO UPDATE
                SET name      = EXCLUDED.name,
                    region_id = EXCLUDED.region_id
            """;
        cmd.Parameters.AddWithValue("id", e.ConstellationId);
        cmd.Parameters.AddWithValue("name", e.Name);
        cmd.Parameters.AddWithValue("regionId", e.RegionId);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task WriteRangeAsync(IList<Constellation> entities)
    {
        if (entities.Count == 0) return;
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        foreach (var e in entities)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO constellations (id, name, region_id)
                VALUES (@id, @name, @regionId)
                ON CONFLICT (id) DO UPDATE
                    SET name      = EXCLUDED.name,
                        region_id = EXCLUDED.region_id
                """;
            cmd.Parameters.AddWithValue("id", e.ConstellationId);
            cmd.Parameters.AddWithValue("name", e.Name);
            cmd.Parameters.AddWithValue("regionId", e.RegionId);
            await cmd.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
    }
}
