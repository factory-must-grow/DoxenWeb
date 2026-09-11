using Npgsql;

namespace Doxen.Data;

public sealed record AdminUserRow(
    long UserId,
    string Email,
    int PlanId,
    string PlanName,
    int DocumentsThisMonth,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LockoutEnd);

public sealed record AdminUserPage(IReadOnlyList<AdminUserRow> Rows, int TotalCount);

// Список пользователей для /admin/users — обычный SQL-запрос с
// джойном к таблице Identity, читать её напрямую можно (02-database.md).
// Писать в AspNetUsers в обход UserManager нельзя — здесь мы этого
// и не делаем: пишем только в user_profiles (смена тарифа).
public sealed class AdminUserRepository
{
    private readonly Db _db;

    public AdminUserRepository(Db db)
    {
        _db = db;
    }

    public async Task<AdminUserPage> SearchAsync(string? emailSearch, int page, int pageSize,
        CancellationToken ct = default)
    {
        var offset = Math.Max(0, page - 1) * pageSize;
        var pattern = string.IsNullOrWhiteSpace(emailSearch) ? null : $"%{emailSearch.Trim()}%";
        var now = DateTimeOffset.UtcNow;

        await using var connection = await _db.OpenConnectionAsync(ct);

        await using var countCommand = new NpgsqlCommand(
            """
            SELECT count(*)
            FROM user_profiles p
            JOIN "AspNetUsers" u ON u."Id" = p.user_id
            WHERE @pattern::text IS NULL OR u."Email" ILIKE @pattern
            """, connection);
        countCommand.Parameters.AddWithValue("pattern", (object?)pattern ?? DBNull.Value);
        var total = Convert.ToInt32(await countCommand.ExecuteScalarAsync(ct));

        await using var command = new NpgsqlCommand(
            """
            SELECT u."Id", u."Email", pl.id, pl.name,
                   COALESCE(c.documents_generated, 0), p.created_at, u."LockoutEnd"
            FROM user_profiles p
            JOIN "AspNetUsers" u ON u."Id" = p.user_id
            JOIN plans pl ON pl.id = p.plan_id
            LEFT JOIN usage_counters c
                   ON c.user_id = p.user_id AND c.period_year = @year AND c.period_month = @month
            WHERE @pattern::text IS NULL OR u."Email" ILIKE @pattern
            ORDER BY p.created_at DESC
            LIMIT @limit OFFSET @offset
            """, connection);
        command.Parameters.AddWithValue("pattern", (object?)pattern ?? DBNull.Value);
        command.Parameters.AddWithValue("year", now.Year);
        command.Parameters.AddWithValue("month", now.Month);
        command.Parameters.AddWithValue("limit", pageSize);
        command.Parameters.AddWithValue("offset", offset);

        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<AdminUserRow>();
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new AdminUserRow(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetFieldValue<DateTimeOffset>(5),
                reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6)));
        }

        return new AdminUserPage(rows, total);
    }

    public Task ChangePlanAsync(long userId, int planId, CancellationToken ct = default) =>
        _db.ExecuteAsync(
            "UPDATE user_profiles SET plan_id = @planId WHERE user_id = @userId",
            p =>
            {
                p.AddWithValue("userId", userId);
                p.AddWithValue("planId", planId);
            }, ct);
}
