using Npgsql;

namespace TelegramGroupsAdmin.Infrastructure;

/// <summary>
/// PostgreSQL implementation of database connection factory.
/// Creates connections from configured connection string with automatic pooling.
/// </summary>
public class PostgresConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public PostgresConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("PostgreSQL")
            ?? throw new InvalidOperationException("PostgreSQL connection string not configured");
    }

    /// <summary>
    /// Creates a new NpgsqlConnection.
    /// PostgreSQL driver automatically handles connection pooling, so creating
    /// new connection objects is cheap and recommended for concurrent operations.
    /// </summary>
    public NpgsqlConnection CreateConnection()
    {
        return new NpgsqlConnection(_connectionString);
    }
}
