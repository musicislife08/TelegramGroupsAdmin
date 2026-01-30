using Microsoft.Playwright;
using Testcontainers.PostgreSql;

namespace TelegramGroupsAdmin.E2ETests;

/// <summary>
/// Assembly-level fixture that starts PostgreSQL container, Playwright, and shared browser once for all tests.
/// Each test creates its own unique database on this shared container for isolation.
/// Browser contexts provide per-test isolation while sharing the browser process.
/// </summary>
[SetUpFixture]
public class E2EFixture
{
    private static PostgreSqlContainer? _container;
    private static IPlaywright? _playwright;
    private static IBrowser? _sharedBrowser;

    /// <summary>
    /// Gets the base connection string for the shared PostgreSQL container.
    /// Each test should create a unique database and modify this connection string.
    /// </summary>
    public static string BaseConnectionString { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the shared Playwright instance for browser automation.
    /// </summary>
    public static IPlaywright Playwright => _playwright ?? throw new InvalidOperationException("Playwright not initialized");

    /// <summary>
    /// Gets the shared browser instance for all tests.
    /// Each test creates an isolated BrowserContext from this browser.
    /// </summary>
    public static IBrowser Browser => _sharedBrowser ?? throw new InvalidOperationException("Browser not initialized");

    [OneTimeSetUp]
    public async Task GlobalSetup()
    {
        // Clear test artifacts from previous runs
        ClearArtifactsDirectory();

        // Start PostgreSQL container
        _container = new PostgreSqlBuilder("postgres:18")
            .WithCleanUp(true)
            .Build();

        await _container.StartAsync();
        BaseConnectionString = _container.GetConnectionString();
        Console.WriteLine($"PostgreSQL container started: {BaseConnectionString}");

        // Initialize Playwright
        _playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        Console.WriteLine("Playwright initialized");

        // Launch browser once for all tests (saves ~500ms per test)
        // Each test creates an isolated BrowserContext for cookie/storage isolation
        _sharedBrowser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        Console.WriteLine("Shared browser launched");
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

        // Close shared browser before disposing Playwright
        if (_sharedBrowser != null)
        {
            await _sharedBrowser.CloseAsync();
            Console.WriteLine("Shared browser closed");
        }

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
