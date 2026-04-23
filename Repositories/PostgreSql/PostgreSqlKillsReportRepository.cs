using EveStatsCollector.Models;
using Npgsql;

namespace EveStatsCollector.Repositories.PostgreSql;

internal sealed class PostgreSqlKillsReportRepository : IKillsReportRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgreSqlKillsReportRepository(NpgsqlDataSource dataSource) =>
        _dataSource = dataSource;

    public async Task<KillsReport> AddAsync(DateTimeOffset lastModified, IReadOnlyList<SystemKills> entries)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        int reportId;
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO kills_reports (last_modified, fetched_at)
                VALUES (@lastModified, @fetchedAt)
                RETURNING id
                """;
            var fetchedAt = DateTimeOffset.UtcNow;
            cmd.Parameters.AddWithValue("lastModified", lastModified);
            cmd.Parameters.AddWithValue("fetchedAt", fetchedAt);
            reportId = (int)(await cmd.ExecuteScalarAsync())!;
        }

        if (entries.Count > 0)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO kills_entries (report_id, system_id, ship_kills, npc_kills, pod_kills)
                SELECT @reportId, unnest(@systemIds), unnest(@shipKills), unnest(@npcKills), unnest(@podKills)
                """;
            cmd.Parameters.AddWithValue("reportId", reportId);
            cmd.Parameters.AddWithValue("systemIds", entries.Select(e => e.SystemId).ToArray());
            cmd.Parameters.AddWithValue("shipKills", entries.Select(e => e.ShipKills).ToArray());
            cmd.Parameters.AddWithValue("npcKills", entries.Select(e => e.NpcKills).ToArray());
            cmd.Parameters.AddWithValue("podKills", entries.Select(e => e.PodKills).ToArray());
            await cmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
        return new KillsReport(reportId, lastModified, DateTimeOffset.UtcNow, entries);
    }

    public async Task<KillsReport?> GetByIdAsync(int id)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        return await QueryReportAsync(conn, "WHERE r.id = @id", cmd => cmd.Parameters.AddWithValue("id", id));
    }

    public async Task<KillsReport?> GetLatestAsync()
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        return await QueryReportAsync(conn, "WHERE r.id = (SELECT MAX(id) FROM kills_reports)", _ => { });
    }

    public async Task<IReadOnlyList<KillsReport>> GetAllAsync()
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT r.id, r.last_modified, r.fetched_at,
                   e.system_id, e.ship_kills, e.npc_kills, e.pod_kills
            FROM kills_reports r
            JOIN kills_entries e ON e.report_id = r.id
            ORDER BY r.id
            """;
        return await ReadReportsAsync(cmd);
    }

    private static async Task<KillsReport?> QueryReportAsync(
        NpgsqlConnection conn, string whereClause, Action<NpgsqlCommand> addParams)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT r.id, r.last_modified, r.fetched_at,
                   e.system_id, e.ship_kills, e.npc_kills, e.pod_kills
            FROM kills_reports r
            JOIN kills_entries e ON e.report_id = r.id
            {whereClause}
            """;
        addParams(cmd);
        var results = await ReadReportsAsync(cmd);
        return results.Count > 0 ? results[0] : null;
    }

    private static async Task<IReadOnlyList<KillsReport>> ReadReportsAsync(NpgsqlCommand cmd)
    {
        await using var reader = await cmd.ExecuteReaderAsync();
        var reports = new Dictionary<int, (DateTimeOffset lastModified, DateTimeOffset fetchedAt, List<SystemKills> entries)>();

        while (await reader.ReadAsync())
        {
            var id = reader.GetInt32(0);
            var lastModified = reader.GetFieldValue<DateTimeOffset>(1);
            var fetchedAt = reader.GetFieldValue<DateTimeOffset>(2);
            var entry = new SystemKills(reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6));

            if (!reports.TryGetValue(id, out var bucket))
                reports[id] = bucket = (lastModified, fetchedAt, []);
            bucket.entries.Add(entry);
        }

        return reports
            .OrderBy(kv => kv.Key)
            .Select(kv => new KillsReport(kv.Key, kv.Value.lastModified, kv.Value.fetchedAt, kv.Value.entries))
            .ToList();
    }
}
