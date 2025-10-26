using Microsoft.EntityFrameworkCore;
using Npgsql;
using TelegramGroupsAdmin.Data;

namespace TelegramGroupsAdmin.Tests.TestHelpers;

/// <summary>
/// Helper class for database migration testing.
/// Provides utilities to create unique test databases, apply migrations, seed data, and cleanup.
/// </summary>
public class MigrationTestHelper : IDisposable
{
    private readonly string _databaseName;
    private readonly string _connectionString;
    private bool _disposed;

    public string DatabaseName => _databaseName;
    public string ConnectionString => _connectionString;

    public MigrationTestHelper()
    {
        // Each test gets a unique database on the shared container
        _databaseName = PostgresFixture.GetUniqueDatabaseName();

        // Build connection string with unique database name
        var builder = new NpgsqlConnectionStringBuilder(PostgresFixture.BaseConnectionString)
        {
            Database = _databaseName
        };
        _connectionString = builder.ConnectionString;
    }

    /// <summary>
    /// Creates the test database and applies all EF Core migrations.
    /// Call this in test setup to get a fresh database with schema applied.
    /// </summary>
    public async Task CreateDatabaseAndApplyMigrationsAsync()
    {
        // First, create the database using the postgres database connection
        var adminBuilder = new NpgsqlConnectionStringBuilder(PostgresFixture.BaseConnectionString)
        {
            Database = "postgres" // Connect to default postgres DB to create our test DB
        };

        await using (var connection = new NpgsqlConnection(adminBuilder.ConnectionString))
        {
            await connection.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", connection);
            await cmd.ExecuteNonQueryAsync();
        }

        // Now apply migrations to the new database
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(_connectionString);

        await using var context = new AppDbContext(optionsBuilder.Options);
        await context.Database.MigrateAsync();
    }

    /// <summary>
    /// Gets a new AppDbContext instance connected to this test's database.
    /// Caller is responsible for disposing the context.
    /// </summary>
    public AppDbContext GetDbContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(_connectionString);
        return new AppDbContext(optionsBuilder.Options);
    }

    /// <summary>
    /// Executes raw SQL against the test database.
    /// Useful for seeding data or verifying results directly.
    /// </summary>
    public async Task ExecuteSqlAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Executes a scalar SQL query and returns the result.
    /// Useful for counting rows or checking specific values.
    /// </summary>
    public async Task<object?> ExecuteScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, connection);
        return await cmd.ExecuteScalarAsync();
    }

    /// <summary>
    /// Drops the test database. Called automatically by Dispose.
    /// </summary>
    private async Task DropDatabaseAsync()
    {
        // Connect to postgres DB to drop our test database
        var adminBuilder = new NpgsqlConnectionStringBuilder(PostgresFixture.BaseConnectionString)
        {
            Database = "postgres"
        };

        await using var connection = new NpgsqlConnection(adminBuilder.ConnectionString);
        await connection.OpenAsync();

        // Terminate any existing connections to the database
        await using var terminateCmd = new NpgsqlCommand(
            $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{_databaseName}'",
            connection);
        await terminateCmd.ExecuteNonQueryAsync();

        // Drop the database
        await using var dropCmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\"", connection);
        await dropCmd.ExecuteNonQueryAsync();
    }

    public void Dispose()
    {
        if (_disposed) return;

        // Only drop database if we have a valid connection string
        if (!string.IsNullOrEmpty(PostgresFixture.BaseConnectionString))
        {
            // Drop database synchronously in Dispose
            DropDatabaseAsync().GetAwaiter().GetResult();
        }

        _disposed = true;
    }
}
