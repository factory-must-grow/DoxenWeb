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
