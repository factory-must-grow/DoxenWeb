using Npgsql;

namespace Doxen.Data;

// Фабрика подключений к PostgreSQL и минимальные хелперы поверх чистого
// Npgsql — без ORM. Каждый вызов открывает своё подключение и закрывает
// его через await using; пул соединений Npgsql делает это дешёвым.
public sealed class Db
{
    private readonly string _connectionString;

    public Db(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct = default)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    public async Task ExecuteAsync(string sql, Action<NpgsqlParameterCollection>? bindParameters = null,
        CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        bindParameters?.Invoke(command.Parameters);
        await command.ExecuteNonQueryAsync(ct);
    }
}
