using Microsoft.Playwright;
using Testcontainers.PostgreSql;

namespace TelegramGroupsAdmin.E2ETests;

/// <summary>
/// Assembly-level fixture that starts PostgreSQL container and installs Playwright browsers once for all tests.
/// Each test creates its own unique database on this shared container for isolation.
/// </summary>
[SetUpFixture]
public class E2EFixture
{
    private static PostgreSqlContainer? _container;
    private static IPlaywright? _playwright;

    /// <summary>
    /// Gets the base connection string for the shared PostgreSQL container.
    /// Each test should create a unique database and modify this connection string.
    /// </summary>
    public static string BaseConnectionString { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the shared Playwright instance for browser automation.
    /// </summary>
    public static IPlaywright Playwright => _playwright ?? throw new InvalidOperationException("Playwright not initialized");

    [OneTimeSetUp]
    public async Task GlobalSetup()
    {
        // Start PostgreSQL container
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:17")
            .WithCleanUp(true)
            .Build();

        await _container.StartAsync();
        BaseConnectionString = _container.GetConnectionString();
        Console.WriteLine($"PostgreSQL container started: {BaseConnectionString}");

        // Initialize Playwright
        _playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        Console.WriteLine("Playwright initialized");
    }

    [OneTimeTearDown]
    public async Task GlobalTeardown()
    {
        _playwright?.Dispose();
        Console.WriteLine("Playwright disposed");

        if (_container != null)
        {
            await _container.DisposeAsync();
            Console.WriteLine("PostgreSQL container stopped and cleaned up");
        }
    }

    /// <summary>
    /// Creates a unique database name for a test to ensure complete isolation.
    /// </summary>
    public static string GetUniqueDatabaseName() => $"e2e_test_{Guid.NewGuid():N}";
}
