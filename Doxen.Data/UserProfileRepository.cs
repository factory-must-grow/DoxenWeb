using Npgsql;

namespace Doxen.Data;

public sealed record UserProfile(long UserId, int PlanId, string PlanName, DateTimeOffset CreatedAt);

// Бизнес-данные пользователя (тариф, дата регистрации). Сама учётная
// запись живёт в AspNetUsers, которой владеет Identity — см.
// 01-architecture.md, раздел про разделение владения таблицами.
public sealed class UserProfileRepository
{
    private readonly Db _db;

    public UserProfileRepository(Db db)
    {
        _db = db;
    }

    public Task CreateAsync(long userId, int planId, CancellationToken ct = default) =>
        _db.ExecuteAsync(
            "INSERT INTO user_profiles (user_id, plan_id) VALUES (@userId, @planId)",
            p =>
            {
                p.AddWithValue("userId", userId);
                p.AddWithValue("planId", planId);
            }, ct);

    public async Task<UserProfile?> GetAsync(long userId, CancellationToken ct = default)
    {
        await using var connection = await _db.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand(
            """
            SELECT p.user_id, p.plan_id, pl.name, p.created_at
            FROM user_profiles p
            JOIN plans pl ON pl.id = p.plan_id
            WHERE p.user_id = @userId
            """, connection);
        command.Parameters.AddWithValue("userId", userId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new UserProfile(
            reader.GetInt64(0),
            reader.GetInt32(1),
            reader.GetString(2),
            reader.GetFieldValue<DateTimeOffset>(3));
    }
}
