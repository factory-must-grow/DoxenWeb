using Npgsql;

namespace Doxen.Data;

// Журнал операций (02-database.md). В журнал не пишется ничего, что
// относится к содержимому — ни путей переменных, ни имени файла:
// только размеры, количества и результат.
public sealed class GenerationLogRepository
{
    private readonly Db _db;

    public GenerationLogRepository(Db db)
    {
        _db = db;
    }

    public Task InsertAsync(long? userId, int templateSizeBytes, int documentsCount, int variablesCount,
        long substitutionsCount, bool succeeded, string? errorMessage, CancellationToken ct = default) =>
        _db.ExecuteAsync(
            """
            INSERT INTO generation_log (user_id, template_size_bytes, documents_count, variables_count,
                                        substitutions_count, succeeded, error_message)
            VALUES (@userId, @size, @docs, @vars, @subs, @ok, @err)
            """,
            p =>
            {
                p.AddWithValue("userId", (object?)userId ?? DBNull.Value);
                p.AddWithValue("size", templateSizeBytes);
                p.AddWithValue("docs", documentsCount);
                p.AddWithValue("vars", variablesCount);
                p.AddWithValue("subs", substitutionsCount);
                p.AddWithValue("ok", succeeded);
                p.AddWithValue("err", (object?)errorMessage ?? DBNull.Value);
            }, ct);
}
