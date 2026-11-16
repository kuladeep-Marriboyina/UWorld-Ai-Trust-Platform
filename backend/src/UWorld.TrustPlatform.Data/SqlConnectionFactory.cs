using Microsoft.Data.SqlClient;

namespace UWorld.TrustPlatform.Data;

public interface ISqlConnectionFactory
{
    Task<SqlConnection> OpenAsync(CancellationToken ct = default);
}

public sealed class SqlConnectionFactory : ISqlConnectionFactory
{
    private readonly string _connectionString;

    public SqlConnectionFactory(string connectionString) => _connectionString = connectionString;

    public async Task<SqlConnection> OpenAsync(CancellationToken ct = default)
    {
        var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }
}
