using Npgsql;

namespace Doxen.Data;

public sealed record UsageSummary(int DocumentsGenerated, long SubstitutionsTotal);

// Счётчики использования за месяц (02-database.md). Инкремент —
// атомарный upsert, без чтения-изменения-записи.
public sealed class UsageRepository
{
    private readonly Db _db;

    public UsageRepository(Db db)
    {
        _db = db;
    }

    public async Task<UsageSummary> GetCurrentMonthAsync(long userId, int year, int month,
        CancellationToken ct = default)
    {
        await using var connection = await _db.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand(
            """
            SELECT documents_generated, substitutions_total
            FROM usage_counters
            WHERE user_id = @userId AND period_year = @year AND period_month = @month
            """, connection);
        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("year", year);
        command.Parameters.AddWithValue("month", month);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return new UsageSummary(0, 0);
        }

        return new UsageSummary(reader.GetInt32(0), reader.GetInt64(1));
    }

    // Инкрементируется только при успешной генерации, одной атомарной
    // операцией; при пакете — на число успешно собранных документов,
    // а не на размер пакета (05-screens.md).
    public Task IncrementAsync(long userId, int year, int month, int documentsCount, long substitutionsCount,
        CancellationToken ct = default) =>
        _db.ExecuteAsync(
            """
            INSERT INTO usage_counters (user_id, period_year, period_month, documents_generated, substitutions_total)
            VALUES (@userId, @year, @month, @docs, @subs)
            ON CONFLICT (user_id, period_year, period_month) DO UPDATE
            SET documents_generated = usage_counters.documents_generated + @docs,
                substitutions_total = usage_counters.substitutions_total + @subs
            """,
            p =>
            {
                p.AddWithValue("userId", userId);
                p.AddWithValue("year", year);
                p.AddWithValue("month", month);
                p.AddWithValue("docs", documentsCount);
                p.AddWithValue("subs", substitutionsCount);
            }, ct);
}
