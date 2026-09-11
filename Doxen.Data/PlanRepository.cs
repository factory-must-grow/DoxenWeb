using Npgsql;

namespace Doxen.Data;

public sealed class PlanRepository
{
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
}
