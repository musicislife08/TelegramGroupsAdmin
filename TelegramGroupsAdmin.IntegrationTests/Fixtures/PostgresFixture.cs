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

    [OneTimeSetUp]
    public async Task GlobalSetup()
    {
        // Start a single Postgres 17 container for all tests
        _container = new PostgreSqlBuilder("postgres:17")
            .WithCleanUp(true)
            .Build();

        await _container.StartAsync();

        BaseConnectionString = _container.GetConnectionString();

        Console.WriteLine($"PostgreSQL container started: {BaseConnectionString}");
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
