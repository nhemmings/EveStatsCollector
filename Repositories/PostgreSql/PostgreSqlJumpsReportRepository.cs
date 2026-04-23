using EveStatsCollector.Models;
using Npgsql;

namespace EveStatsCollector.Repositories.PostgreSql;

internal sealed class PostgreSqlJumpsReportRepository : IJumpsReportRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgreSqlJumpsReportRepository(NpgsqlDataSource dataSource) =>
        _dataSource = dataSource;

    public async Task<JumpsReport> AddAsync(DateTimeOffset lastModified, IReadOnlyList<SystemJumps> entries)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        int reportId;
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO jumps_reports (last_modified, fetched_at)
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
                INSERT INTO jumps_entries (report_id, system_id, ship_jumps)
                SELECT @reportId, unnest(@systemIds), unnest(@shipJumps)
                """;
            cmd.Parameters.AddWithValue("reportId", reportId);
            cmd.Parameters.AddWithValue("systemIds", entries.Select(e => e.SystemId).ToArray());
            cmd.Parameters.AddWithValue("shipJumps", entries.Select(e => e.ShipJumps).ToArray());
            await cmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
        return new JumpsReport(reportId, lastModified, DateTimeOffset.UtcNow, entries);
    }

    public async Task<JumpsReport?> GetByIdAsync(int id)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        return await QueryReportAsync(conn, "WHERE r.id = @id", cmd => cmd.Parameters.AddWithValue("id", id));
    }

    public async Task<JumpsReport?> GetLatestAsync()
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        return await QueryReportAsync(conn, "WHERE r.id = (SELECT MAX(id) FROM jumps_reports)", _ => { });
    }

    public async Task<IReadOnlyList<JumpsReport>> GetAllAsync()
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT r.id, r.last_modified, r.fetched_at,
                   e.system_id, e.ship_jumps
            FROM jumps_reports r
            JOIN jumps_entries e ON e.report_id = r.id
            ORDER BY r.id
            """;
        return await ReadReportsAsync(cmd);
    }

    private static async Task<JumpsReport?> QueryReportAsync(
        NpgsqlConnection conn, string whereClause, Action<NpgsqlCommand> addParams)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT r.id, r.last_modified, r.fetched_at,
                   e.system_id, e.ship_jumps
            FROM jumps_reports r
            JOIN jumps_entries e ON e.report_id = r.id
            {whereClause}
            """;
        addParams(cmd);
        var results = await ReadReportsAsync(cmd);
        return results.Count > 0 ? results[0] : null;
    }

    private static async Task<IReadOnlyList<JumpsReport>> ReadReportsAsync(NpgsqlCommand cmd)
    {
        await using var reader = await cmd.ExecuteReaderAsync();
        var reports = new Dictionary<int, (DateTimeOffset lastModified, DateTimeOffset fetchedAt, List<SystemJumps> entries)>();

        while (await reader.ReadAsync())
        {
            var id = reader.GetInt32(0);
            var lastModified = reader.GetFieldValue<DateTimeOffset>(1);
            var fetchedAt = reader.GetFieldValue<DateTimeOffset>(2);
            var entry = new SystemJumps(reader.GetInt32(3), reader.GetInt32(4));

            if (!reports.TryGetValue(id, out var bucket))
                reports[id] = bucket = (lastModified, fetchedAt, []);
            bucket.entries.Add(entry);
        }

        return reports
            .OrderBy(kv => kv.Key)
            .Select(kv => new JumpsReport(kv.Key, kv.Value.lastModified, kv.Value.fetchedAt, kv.Value.entries))
            .ToList();
    }
}
