using System.Collections.Concurrent;
using EveStatsCollector.Models;
using Npgsql;

namespace EveStatsCollector.Repositories.PostgreSql;

internal sealed class PostgreSqlRegionRepository : IRegionRepository, IAsyncPreloadable
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ConcurrentDictionary<int, Region> _cache = new();

    public PostgreSqlRegionRepository(NpgsqlDataSource dataSource) =>
        _dataSource = dataSource;

    public async Task LoadAsync(CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT r.id, r.name, ARRAY_REMOVE(ARRAY_AGG(c.id ORDER BY c.id), NULL)
            FROM regions r
            LEFT JOIN constellations c ON c.region_id = r.id
            GROUP BY r.id, r.name
            """;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            _cache[reader.GetInt32(0)] = new Region(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetFieldValue<int[]>(2));
    }

    public Region? GetById(int id) => _cache.TryGetValue(id, out var v) ? v : null;
    public IReadOnlyList<Region> GetAll() => _cache.Values.ToList();

    public void Upsert(Region entity)
    {
        _cache[entity.RegionId] = entity;
        WriteAsync(entity).GetAwaiter().GetResult();
    }

    public void UpsertRange(IEnumerable<Region> entities)
    {
        var list = entities.ToList();
        foreach (var e in list)
            _cache[e.RegionId] = e;
        WriteRangeAsync(list).GetAwaiter().GetResult();
    }

    private async Task WriteAsync(Region e)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO regions (id, name)
            VALUES (@id, @name)
            ON CONFLICT (id) DO UPDATE
                SET name = EXCLUDED.name
            """;
        cmd.Parameters.AddWithValue("id", e.RegionId);
        cmd.Parameters.AddWithValue("name", e.Name);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task WriteRangeAsync(IList<Region> entities)
    {
        if (entities.Count == 0) return;
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        foreach (var e in entities)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO regions (id, name)
                VALUES (@id, @name)
                ON CONFLICT (id) DO UPDATE
                    SET name = EXCLUDED.name
                """;
            cmd.Parameters.AddWithValue("id", e.RegionId);
            cmd.Parameters.AddWithValue("name", e.Name);
            await cmd.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
    }
}
