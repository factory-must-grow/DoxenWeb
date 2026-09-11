using Npgsql;

namespace Doxen.Data;

public sealed record Plan(
    int Id,
    string Code,
    string Name,
    int PriceRub,
    int MaxDocumentsPerMonth,
    int MaxVariablesPerTemplate,
    int MaxFileSizeMb,
    bool AllowBatch,
    bool IsActive);

// AttachedUsersCount > 0, когда удаление отклонено — тариф ещё
// используется, сначала нужно перевести пользователей на другой
// (05-screens.md).
public sealed record DeletePlanResult(bool Success, int AttachedUsersCount);

public sealed class PlanRepository
{
    private const string SelectColumns =
        """
        SELECT id, code, name, price_rub, max_documents_per_month,
               max_variables_per_template, max_file_size_mb, allow_batch, is_active
        FROM plans
        """;

    private readonly Db _db;

    public PlanRepository(Db db)
    {
        _db = db;
    }

    public async Task<int?> GetIdByCodeAsync(string code, CancellationToken ct = default)
    {
        await using var connection = await _db.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand("SELECT id FROM plans WHERE code = @code", connection);
        command.Parameters.AddWithValue("code", code);

        var result = await command.ExecuteScalarAsync(ct);
        return result is null ? null : Convert.ToInt32(result);
    }

    public async Task<Plan?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        await using var connection = await _db.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand(SelectColumns + " WHERE code = @code", connection);
        command.Parameters.AddWithValue("code", code);

        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadPlan(reader) : null;
    }

    public async Task<Plan?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using var connection = await _db.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand(SelectColumns + " WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", id);

        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadPlan(reader) : null;
    }

    public async Task<IReadOnlyList<Plan>> GetAllAsync(CancellationToken ct = default)
    {
        await using var connection = await _db.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand(SelectColumns + " ORDER BY sort_order", connection);

        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<Plan>();
        while (await reader.ReadAsync(ct))
        {
            result.Add(ReadPlan(reader));
        }

        return result;
    }

    public async Task<int> CreateAsync(string code, string name, int priceRub, int maxDocumentsPerMonth,
        int maxVariablesPerTemplate, int maxFileSizeMb, bool allowBatch, bool isActive, CancellationToken ct = default)
    {
        await using var connection = await _db.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO plans (code, name, price_rub, max_documents_per_month, max_variables_per_template,
                               max_file_size_mb, allow_batch, is_active)
            VALUES (@code, @name, @price, @maxDocs, @maxVars, @maxFileSize, @allowBatch, @isActive)
            RETURNING id
            """, connection);
        command.Parameters.AddWithValue("code", code);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("price", priceRub);
        command.Parameters.AddWithValue("maxDocs", maxDocumentsPerMonth);
        command.Parameters.AddWithValue("maxVars", maxVariablesPerTemplate);
        command.Parameters.AddWithValue("maxFileSize", maxFileSizeMb);
        command.Parameters.AddWithValue("allowBatch", allowBatch);
        command.Parameters.AddWithValue("isActive", isActive);

        var id = await command.ExecuteScalarAsync(ct);
        return Convert.ToInt32(id);
    }

    public Task UpdateAsync(int id, string name, int priceRub, int maxDocumentsPerMonth,
        int maxVariablesPerTemplate, int maxFileSizeMb, bool allowBatch, bool isActive,
        CancellationToken ct = default) =>
        _db.ExecuteAsync(
            """
            UPDATE plans
            SET name = @name, price_rub = @price, max_documents_per_month = @maxDocs,
                max_variables_per_template = @maxVars, max_file_size_mb = @maxFileSize,
                allow_batch = @allowBatch, is_active = @isActive
            WHERE id = @id
            """,
            p =>
            {
                p.AddWithValue("id", id);
                p.AddWithValue("name", name);
                p.AddWithValue("price", priceRub);
                p.AddWithValue("maxDocs", maxDocumentsPerMonth);
                p.AddWithValue("maxVars", maxVariablesPerTemplate);
                p.AddWithValue("maxFileSize", maxFileSizeMb);
                p.AddWithValue("allowBatch", allowBatch);
                p.AddWithValue("isActive", isActive);
            }, ct);

    // Удаление тарифа, к которому привязаны пользователи, запрещено —
    // предложить сначала перевести их (05-screens.md).
    public async Task<DeletePlanResult> DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var connection = await _db.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        await using var countCommand = new NpgsqlCommand(
            "SELECT count(*) FROM user_profiles WHERE plan_id = @id", connection, transaction);
        countCommand.Parameters.AddWithValue("id", id);
        var attached = (long)(await countCommand.ExecuteScalarAsync(ct))!;

        if (attached > 0)
        {
            await transaction.RollbackAsync(ct);
            return new DeletePlanResult(false, (int)attached);
        }

        await using var deleteCommand = new NpgsqlCommand("DELETE FROM plans WHERE id = @id", connection, transaction);
        deleteCommand.Parameters.AddWithValue("id", id);
        await deleteCommand.ExecuteNonQueryAsync(ct);

        await transaction.CommitAsync(ct);
        return new DeletePlanResult(true, 0);
    }

    private static Plan ReadPlan(NpgsqlDataReader reader) => new(
        reader.GetInt32(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetInt32(3),
        reader.GetInt32(4),
        reader.GetInt32(5),
        reader.GetInt32(6),
        reader.GetBoolean(7),
        reader.GetBoolean(8));
}
