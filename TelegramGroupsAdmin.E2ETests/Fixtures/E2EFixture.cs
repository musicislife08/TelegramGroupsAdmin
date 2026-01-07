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
        // Clear test artifacts from previous runs
        ClearArtifactsDirectory();

        // Start PostgreSQL container
        _container = new PostgreSqlBuilder("postgres:17")
            .WithCleanUp(true)
            .Build();

        await _container.StartAsync();
        BaseConnectionString = _container.GetConnectionString();
        Console.WriteLine($"PostgreSQL container started: {BaseConnectionString}");

        // Initialize Playwright
        _playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        Console.WriteLine("Playwright initialized");
    }

    /// <summary>
    /// Clears the test-artifacts directory at the start of each test run.
    /// </summary>
    private static void ClearArtifactsDirectory()
    {
        var artifactsDir = Path.Combine(TestContext.CurrentContext.WorkDirectory, "test-artifacts");
        if (Directory.Exists(artifactsDir))
        {
            Directory.Delete(artifactsDir, recursive: true);
            Console.WriteLine("Cleared test-artifacts directory");
        }
    }

    [OneTimeTearDown]
    public async Task GlobalTeardown()
    {
        // Dispose shared factory first (stops Kestrel server, cleans up database)
        // This MUST happen before disposing Playwright/container, otherwise the server hangs
        SharedE2ETestBase.DisposeSharedFactory();
        Console.WriteLine("Shared WebApplicationFactory disposed");

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
