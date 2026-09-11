using Npgsql;

namespace Doxen.Data;

// Версионирование схемы бизнес-таблиц: таблица schema_version хранит
// номер текущей версии, массив Steps — миграции по порядку. Индекс в
// массиве плюс единица равен номеру версии. Существующие элементы
// массива никогда не редактируются — только дописываются новые в конец.
public static class Migrations
{
    private static readonly string[] Steps =
    {
        // 1: тарифы, профили пользователей, счётчики использования, журнал операций
        """
        CREATE TABLE plans (
            id                          serial PRIMARY KEY,
            code                        text NOT NULL UNIQUE,
            name                        text NOT NULL,
            price_rub                   integer NOT NULL DEFAULT 0,
            max_documents_per_month     integer NOT NULL,
            max_variables_per_template  integer NOT NULL,
            max_file_size_mb            integer NOT NULL,
            allow_batch                 boolean NOT NULL DEFAULT false,
            is_active                   boolean NOT NULL DEFAULT true,
            sort_order                  integer NOT NULL DEFAULT 0
        );

        INSERT INTO plans (code, name, price_rub, max_documents_per_month,
                           max_variables_per_template, max_file_size_mb, allow_batch, sort_order)
        VALUES ('free', 'Free', 0, 1000000, 1000000, 50, false, 0);

        CREATE TABLE user_profiles (
            user_id     bigint PRIMARY KEY,
            plan_id     integer NOT NULL REFERENCES plans(id),
            created_at  timestamptz NOT NULL DEFAULT now()
        );

        CREATE TABLE usage_counters (
            user_id             bigint NOT NULL REFERENCES user_profiles(user_id) ON DELETE CASCADE,
            period_year         integer NOT NULL,
            period_month        integer NOT NULL,
            documents_generated integer NOT NULL DEFAULT 0,
            substitutions_total bigint NOT NULL DEFAULT 0,
            PRIMARY KEY (user_id, period_year, period_month)
        );

        CREATE TABLE generation_log (
            id                  bigserial PRIMARY KEY,
            user_id             bigint REFERENCES user_profiles(user_id) ON DELETE SET NULL,
            created_at          timestamptz NOT NULL DEFAULT now(),
            template_size_bytes integer NOT NULL DEFAULT 0,
            documents_count     integer NOT NULL DEFAULT 1,
            variables_count     integer NOT NULL DEFAULT 0,
            substitutions_count integer NOT NULL DEFAULT 0,
            succeeded           boolean NOT NULL,
            error_message       text
        );

        CREATE INDEX ix_generation_log_user_created ON generation_log (user_id, created_at DESC);
        """,
    };

    public static async Task ApplyAsync(string connectionString, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);

        await using (var ensureTable = new NpgsqlCommand(
            "CREATE TABLE IF NOT EXISTS schema_version (version integer NOT NULL)", connection))
        {
            await ensureTable.ExecuteNonQueryAsync(ct);
        }

        var currentVersion = await ReadCurrentVersionAsync(connection, ct);
        var hasVersionRow = currentVersion.HasValue;
        var version = currentVersion ?? 0;

        for (var i = version; i < Steps.Length; i++)
        {
            await using var transaction = await connection.BeginTransactionAsync(ct);

            await using (var applyStep = new NpgsqlCommand(Steps[i], connection, transaction))
            {
                await applyStep.ExecuteNonQueryAsync(ct);
            }

            var newVersion = i + 1;
            var versionSql = hasVersionRow
                ? "UPDATE schema_version SET version = @v"
                : "INSERT INTO schema_version (version) VALUES (@v)";

            await using (var updateVersion = new NpgsqlCommand(versionSql, connection, transaction))
            {
                updateVersion.Parameters.AddWithValue("v", newVersion);
                await updateVersion.ExecuteNonQueryAsync(ct);
            }

            await transaction.CommitAsync(ct);
            hasVersionRow = true;
        }
    }

    private static async Task<int?> ReadCurrentVersionAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT version FROM schema_version LIMIT 1", connection);
        var result = await command.ExecuteScalarAsync(ct);
        return result is null ? null : Convert.ToInt32(result);
    }
}
