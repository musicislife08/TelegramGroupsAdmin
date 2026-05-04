using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestData;
using Testcontainers.PostgreSql;

namespace TelegramGroupsAdmin.IntegrationTests;

/// <summary>
/// Shared PostgreSQL container fixture - starts once for all tests in the assembly.
/// Each test creates its own unique database on this shared container for perfect isolation.
/// </summary>
[SetUpFixture]
public class PostgresFixture
{
    private static PostgreSqlContainer? _container;

    /// <summary>
    /// Gets the connection string for the shared Postgres container.
    /// Each test should create a unique database name and replace the database in this connection string.
    /// </summary>
    public static string BaseConnectionString { get; private set; } = string.Empty;

    /// <summary>
    /// A single ephemeral DataProtection provider shared across the entire test session.
    /// Used by canonical-consumer tests so encrypted-column ciphertext written into
    /// the golden_template (via LoadCanonicalAsync) can be decrypted by tests at runtime.
    /// </summary>
    public static IDataProtectionProvider SharedDataProtectionProvider { get; }
        = new EphemeralDataProtectionProvider();

    [OneTimeSetUp]
    public async Task GlobalSetup()
    {
        // Start a single Postgres 18 container for all tests
        _container = new PostgreSqlBuilder("postgres:18")
            .WithCleanUp(true)
            .Build();

        await _container.StartAsync();

        BaseConnectionString = _container.GetConnectionString();

        Console.WriteLine($"PostgreSQL container started: {BaseConnectionString}");

        await BuildEmptyTemplateAsync();
        await BuildGoldenTemplateAsync();
    }

    private static async Task BuildEmptyTemplateAsync()
    {
        var adminBuilder = new NpgsqlConnectionStringBuilder(BaseConnectionString)
        {
            Database = "postgres",
            Pooling = false,
        };

        // 1. CREATE DATABASE empty_template
        await using (var conn = new NpgsqlConnection(adminBuilder.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("CREATE DATABASE empty_template", conn);
            await cmd.ExecuteNonQueryAsync();
        }

        // 2. Apply migrations to empty_template
        var emptyBuilder = new NpgsqlConnectionStringBuilder(BaseConnectionString)
        {
            Database = "empty_template",
            Pooling = false,
        };

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(emptyBuilder.ConnectionString);
        await using (var ctx = new AppDbContext(optionsBuilder.Options))
        {
            await ctx.Database.MigrateAsync();
        }

        // 3. Flag as template
        await using (var conn = new NpgsqlConnection(adminBuilder.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "UPDATE pg_database SET datistemplate = true WHERE datname = 'empty_template'",
                conn);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task BuildGoldenTemplateAsync()
    {
        var adminBuilder = new NpgsqlConnectionStringBuilder(BaseConnectionString)
        {
            Database = "postgres",
            Pooling = false,
        };

        // 1. CREATE DATABASE golden_template TEMPLATE empty_template
        await using (var conn = new NpgsqlConnection(adminBuilder.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "CREATE DATABASE golden_template TEMPLATE empty_template",
                conn);
            await cmd.ExecuteNonQueryAsync();
        }

        // 2. Load canonical into golden_template using SharedDataProtectionProvider
        var goldenBuilder = new NpgsqlConnectionStringBuilder(BaseConnectionString)
        {
            Database = "golden_template",
            Pooling = false,
        };

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(goldenBuilder.ConnectionString);
        await using (var ctx = new AppDbContext(optionsBuilder.Options))
        {
            await GoldenDataset.LoadCanonicalAsync(ctx, SharedDataProtectionProvider);
        }

        // 3. Flag as template
        await using (var conn = new NpgsqlConnection(adminBuilder.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "UPDATE pg_database SET datistemplate = true WHERE datname = 'golden_template'",
                conn);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    [OneTimeTearDown]
    public async Task GlobalTeardown()
    {
        if (_container != null)
        {
            await _container.DisposeAsync();
            Console.WriteLine("PostgreSQL container stopped and cleaned up");
        }
    }

    /// <summary>
    /// Creates a unique database name for a test to ensure complete isolation.
    /// </summary>
    public static string GetUniqueDatabaseName() => $"test_db_{Guid.NewGuid():N}";
}
