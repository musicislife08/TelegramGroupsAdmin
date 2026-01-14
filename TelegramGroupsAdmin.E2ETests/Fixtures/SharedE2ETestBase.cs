using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests;

/// <summary>
/// Base class for E2E tests that share a single TestWebApplicationFactory instance.
///
/// IMPORTANT: The factory is shared across ALL test classes that inherit from this base,
/// not just within a single test class. This is intentional for maximum performance.
/// Database isolation between tests is achieved via TRUNCATE (~10-50ms), not factory recreation.
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
    // Shared across ALL test classes that inherit from this base (not per-class)
    // This is intentional for maximum performance - TRUNCATE provides data isolation
    private static TestWebApplicationFactory? _sharedFactory;
    private static HttpClient? _sharedClient;
    private static readonly object _factoryLock = new();

    /// <summary>
    /// Disposes the shared factory. Called by E2EFixture.OneTimeTearDown() at assembly level.
    /// </summary>
    internal static void DisposeSharedFactory()
    {
        lock (_factoryLock)
        {
            _sharedClient?.Dispose();
            _sharedClient = null;

            _sharedFactory?.Dispose();
            _sharedFactory = null;
        }
    }

    /// <summary>
    /// Validates PostgreSQL identifier names to prevent injection (defense-in-depth).
    /// Pattern: Start with letter/underscore, contain only letters/digits/underscores.
    /// </summary>
    private static readonly Regex ValidTableNamePattern = new(@"^[a-zA-Z_][a-zA-Z0-9_]*$", RegexOptions.Compiled);

    /// <summary>
    /// Gets the shared factory instance. Created once and shared across all test classes.
    /// </summary>
    protected static TestWebApplicationFactory SharedFactory
    {
        get => _sharedFactory ?? throw new InvalidOperationException("SharedFactory not initialized. Ensure OneTimeSetUp has run.");
    }

    /// <summary>
    /// Gets the shared HTTP client.
    /// </summary>
    protected HttpClient Client => _sharedClient ?? throw new InvalidOperationException("Client not initialized.");

    // Per-test browser context (browser is shared via E2EFixture)
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

        // Use shared browser, create isolated context per test
        // BrowserContext provides full isolation (cookies, localStorage, cache)
        Context = await E2EFixture.Browser.NewContextAsync(new BrowserNewContextOptions
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

        // Safely close context resources (may be null if setup failed)
        // Note: Browser is shared via E2EFixture - don't close it here
        if (Page != null) await Page.CloseAsync();
        if (Context != null) await Context.CloseAsync();

        // Note: We don't dispose the factory here - it's shared across all tests
    }

    [OneTimeTearDown]
    public virtual async Task OneTimeTearDown()
    {
        // Note: SharedFactory is NOT disposed at class level - it's shared across all test classes.
        // Disposal happens at assembly level via E2EFixture.GlobalTeardown() calling DisposeSharedFactory().
        // This ensures proper cleanup when all tests complete, which is required for Rider's test UI
        // to properly exit (Kestrel server must be stopped).
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

        // Fail fast if no tables found - indicates migration failure
        if (tables.Count == 0)
        {
            throw new InvalidOperationException(
                "No application tables found in database. " +
                "This likely indicates database migrations have not run.");
        }

        // Defense-in-depth: validate table names match PostgreSQL identifier pattern
        // Even though pg_tables is trusted, this guards against edge cases
        var invalidTables = tables.Where(t => !ValidTableNamePattern.IsMatch(t)).ToList();
        if (invalidTables.Count > 0)
        {
            throw new InvalidOperationException(
                $"Invalid table names from pg_tables (unexpected format): {string.Join(", ", invalidTables)}");
        }

        // Build comma-separated list with proper quoting for PostgreSQL
        var tableList = string.Join(", ", tables.Select(t => $"\"{t}\""));

        // TRUNCATE is much faster than DELETE:
        // - Removes all rows without scanning them
        // - RESTART IDENTITY resets auto-increment sequences
        // - CASCADE handles foreign key dependencies automatically
#pragma warning disable EF1002 // Table names validated above and come from system catalog
        await dbContext.Database.ExecuteSqlRawAsync(
            $"TRUNCATE TABLE {tableList} RESTART IDENTITY CASCADE;");
#pragma warning restore EF1002
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
