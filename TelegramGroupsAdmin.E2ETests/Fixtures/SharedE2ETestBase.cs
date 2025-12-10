using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests;

/// <summary>
/// Base class for E2E tests that share a single TestWebApplicationFactory instance per test class.
/// Uses TRUNCATE to reset the database between tests for fast isolation (~10-50ms vs ~2s for DB creation).
///
/// Use this base class for tests that:
/// - Use unique identifiers (emails, user IDs, etc.) that don't conflict
/// - Don't assert on aggregate counts that depend on empty state
/// - Don't modify global configuration settings
///
/// For tests that require complete isolation (Dashboard, Settings, Registration),
/// continue using E2ETestBase which creates a fresh factory per test.
/// </summary>
public abstract class SharedE2ETestBase
{
    // Shared across all tests in the fixture - created once in OneTimeSetUp
    private static TestWebApplicationFactory? _sharedFactory;
    private static HttpClient? _sharedClient;
    private static readonly object _factoryLock = new();

    /// <summary>
    /// Gets the shared factory instance. Created once per test class.
    /// </summary>
    protected static TestWebApplicationFactory SharedFactory
    {
        get => _sharedFactory ?? throw new InvalidOperationException("SharedFactory not initialized. Ensure OneTimeSetUp has run.");
    }

    /// <summary>
    /// Gets the shared HTTP client.
    /// </summary>
    protected HttpClient Client => _sharedClient ?? throw new InvalidOperationException("Client not initialized.");

    // Per-test browser state
    protected IBrowser Browser { get; private set; } = null!;
    protected IBrowserContext Context { get; private set; } = null!;
    protected IPage Page { get; private set; } = null!;

    /// <summary>
    /// Gets the base URL where the Kestrel server is listening.
    /// </summary>
    protected string BaseUrl => SharedFactory.ServerAddress;

    /// <summary>
    /// Gets the test email service for verifying sent emails.
    /// </summary>
    protected TestEmailService EmailService => SharedFactory.EmailService;

    [OneTimeSetUp]
    public virtual async Task OneTimeSetUp()
    {
        // Thread-safe factory creation (in case of parallel test class execution)
        lock (_factoryLock)
        {
            if (_sharedFactory == null)
            {
                _sharedFactory = new TestWebApplicationFactory();
                _sharedFactory.StartServer();
                _sharedClient = new HttpClient { BaseAddress = new Uri(_sharedFactory.ServerAddress) };
            }
        }

        await Task.CompletedTask;
    }

    [SetUp]
    public async Task BaseSetUp()
    {
        // TRUNCATE all tables for clean slate (fast - ~10-50ms)
        await TruncateAllTablesAsync();

        // Clear captured emails from previous tests
        EmailService.Clear();

        // Launch browser and create context (per-test for isolation)
        Browser = await E2EFixture.Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true // Set to false for debugging
        });

        Context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = BaseUrl,
            IgnoreHTTPSErrors = true
        });

        // Start tracing for failure diagnostics (per article recommendation)
        // Traces are only saved on failure - provides DOM snapshots, network, console logs
        await Context.Tracing.StartAsync(new TracingStartOptions
        {
            Screenshots = true,
            Snapshots = true,
            Sources = false // Don't include source files in trace
        });

        Page = await Context.NewPageAsync();
    }

    [TearDown]
    public async Task BaseTearDown()
    {
        var testName = TestContext.CurrentContext.Test.Name;
        var testStatus = TestContext.CurrentContext.Result.Outcome.Status;
        var isFailed = testStatus == NUnit.Framework.Interfaces.TestStatus.Failed;

        // Only capture artifacts (screenshots, traces) when tests fail
        var shouldCaptureArtifacts = isFailed;

        if (Page != null && shouldCaptureArtifacts)
        {
            try
            {
                var artifactsDir = Path.Combine(TestContext.CurrentContext.WorkDirectory, "test-artifacts");
                Directory.CreateDirectory(artifactsDir);

                var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");

                // Save screenshot
                var screenshotPath = Path.Combine(artifactsDir, $"{testName}_{testStatus}_{timestamp}.png");
                await Page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath, FullPage = true });
                TestContext.Out.WriteLine($"Screenshot saved: {screenshotPath}");

                // Save trace on failure (per article: "Capture traces, screenshots, and videos on first retry")
                // Traces are invaluable for debugging - show timeline, DOM snapshots, network calls
                if (isFailed)
                {
                    var tracePath = Path.Combine(artifactsDir, $"{testName}_{timestamp}.zip");
                    await Context.Tracing.StopAsync(new TracingStopOptions { Path = tracePath });
                    TestContext.Out.WriteLine($"Trace saved: {tracePath}");
                    TestContext.Out.WriteLine("View trace at: https://trace.playwright.dev/");
                }
                else
                {
                    // Discard trace for passing tests (don't save to disk)
                    await Context.Tracing.StopAsync();
                }
            }
            catch (Exception ex)
            {
                TestContext.Out.WriteLine($"Failed to capture artifacts: {ex.Message}");
            }
        }
        else if (Context != null)
        {
            // Still need to stop tracing even if not saving
            try
            {
                await Context.Tracing.StopAsync();
            }
            catch (Exception ex)
            {
                TestContext.Out.WriteLine($"Warning: Failed to stop tracing: {ex.Message}");
            }
        }

        // Safely close browser resources (may be null if setup failed)
        if (Page != null) await Page.CloseAsync();
        if (Context != null) await Context.CloseAsync();
        if (Browser != null) await Browser.CloseAsync();

        // Note: We don't dispose the factory here - it's shared across all tests in the class
    }

    [OneTimeTearDown]
    public virtual async Task OneTimeTearDown()
    {
        // Dispose shared resources when all tests in this class are done
        lock (_factoryLock)
        {
            _sharedClient?.Dispose();
            _sharedClient = null;

            if (_sharedFactory != null)
            {
                _sharedFactory.Dispose();
                _sharedFactory = null;
            }
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Truncates all application tables for test isolation.
    /// Uses TRUNCATE ... CASCADE for speed (~10-50ms vs ~2s for full DB creation).
    /// </summary>
    private async Task TruncateAllTablesAsync()
    {
        using var scope = SharedFactory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Query all user tables dynamically (excludes system/Quartz/migration tables)
        // This approach automatically includes new tables as schema evolves
        var tables = await dbContext.Database.SqlQueryRaw<string>(@"
            SELECT tablename FROM pg_tables
            WHERE schemaname = 'public'
            AND tablename NOT LIKE 'qrtz_%'
            AND tablename != '__EFMigrationsHistory'
        ").ToListAsync();

        if (tables.Count > 0)
        {
            // Build comma-separated list with proper quoting for PostgreSQL
            var tableList = string.Join(", ", tables.Select(t => $"\"{t}\""));

            // TRUNCATE is much faster than DELETE:
            // - Removes all rows without scanning them
            // - RESTART IDENTITY resets auto-increment sequences
            // - CASCADE handles foreign key dependencies automatically
            // Note: Table names come from pg_tables system catalog, not user input - safe from injection
#pragma warning disable EF1002 // Table names from system catalog are safe
            await dbContext.Database.ExecuteSqlRawAsync(
                $"TRUNCATE TABLE {tableList} RESTART IDENTITY CASCADE;");
#pragma warning restore EF1002
        }
    }

    /// <summary>
    /// Navigates to a path relative to the base URL.
    /// </summary>
    protected async Task NavigateToAsync(string path)
    {
        var url = path.StartsWith("/") ? path : $"/{path}";
        await Page.GotoAsync(url);
    }

    /// <summary>
    /// Waits for the page to navigate to a specific URL pattern.
    /// </summary>
    protected async Task WaitForUrlAsync(string urlPattern, int timeoutMs = 10000)
    {
        await Page.WaitForURLAsync(urlPattern, new PageWaitForURLOptions
        {
            Timeout = timeoutMs
        });
    }

    /// <summary>
    /// Asserts that the current URL matches the expected path using Playwright's auto-retry.
    /// </summary>
    protected async Task AssertUrlAsync(string expectedPath, int timeoutMs = 10000)
    {
        var path = expectedPath.StartsWith("/") ? expectedPath : $"/{expectedPath}";

        // Use Playwright's auto-retrying assertion
        // Pattern matches the path, allowing for query strings
        await Expect(Page).ToHaveURLAsync(
            new Regex($".*{Regex.Escape(path)}(\\?.*)?$"),
            new() { Timeout = timeoutMs });
    }
}
