using Npgsql;

namespace TelegramGroupsAdmin.Ui.Server.Infrastructure;

/// <summary>
/// Factory for creating database connections.
/// Provides a clean abstraction over connection creation and ensures proper connection management.
/// </summary>
public interface IDbConnectionFactory
{
    /// <summary>
    /// Creates a new PostgreSQL database connection.
    /// Connection is NOT opened - caller must open it.
    /// Use with 'await using' to ensure proper disposal.
    /// </summary>
    /// <returns>A new NpgsqlConnection instance</returns>
    NpgsqlConnection CreateConnection();
}
