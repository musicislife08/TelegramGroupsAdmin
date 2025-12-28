using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests;

/// <summary>
/// Base class for WASM UI E2E tests that share a single WasmTestWebApplicationFactory instance.
/// Similar to SharedE2ETestBase but uses the WASM UI Server instead of the Blazor Server app.
/// </summary>
public abstract class WasmSharedE2ETestBase
{
    // Shared across ALL test classes that inherit from this base
    private static WasmTestWebApplicationFactory? _sharedFactory;
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

    private static readonly Regex ValidTableNamePattern = new(@"^[a-zA-Z_][a-zA-Z0-9_]*$", RegexOptions.Compiled);

    /// <summary>
    /// Gets the shared factory instance.
    /// </summary>
    protected static WasmTestWebApplicationFactory SharedFactory
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
    protected WasmTestEmailService EmailService => SharedFactory.EmailService;

    [OneTimeSetUp]
    public virtual async Task OneTimeSetUp()
    {
        lock (_factoryLock)
        {
            if (_sharedFactory == null)
            {
                _sharedFactory = new WasmTestWebApplicationFactory();
                _sharedFactory.StartServer();
                _sharedClient = new HttpClient { BaseAddress = new Uri(_sharedFactory.ServerAddress) };
            }
        }

        await Task.CompletedTask;
    }

    [SetUp]
    public async Task BaseSetUp()
    {
        // TRUNCATE all tables for clean slate
        await TruncateAllTablesAsync();

        // Clear captured emails from previous tests
        EmailService.Clear();

        // Launch browser and create context
        Browser = await E2EFixture.Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });

        Context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = BaseUrl,
            IgnoreHTTPSErrors = true
        });

        // Start tracing for failure diagnostics
        await Context.Tracing.StartAsync(new TracingStartOptions
        {
            Screenshots = true,
            Snapshots = true,
            Sources = false
        });

        Page = await Context.NewPageAsync();
    }

    [TearDown]
    public async Task BaseTearDown()
    {
        var testName = TestContext.CurrentContext.Test.Name;
        var testStatus = TestContext.CurrentContext.Result.Outcome.Status;
        var isFailed = testStatus == NUnit.Framework.Interfaces.TestStatus.Failed;

        if (Page != null && isFailed)
        {
            try
            {
                var artifactsDir = Path.Combine(TestContext.CurrentContext.WorkDirectory, "test-artifacts");
                Directory.CreateDirectory(artifactsDir);

                var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");

                var screenshotPath = Path.Combine(artifactsDir, $"WASM_{testName}_{testStatus}_{timestamp}.png");
                await Page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath, FullPage = true });
                TestContext.Out.WriteLine($"Screenshot saved: {screenshotPath}");

                var tracePath = Path.Combine(artifactsDir, $"WASM_{testName}_{timestamp}.zip");
                await Context.Tracing.StopAsync(new TracingStopOptions { Path = tracePath });
                TestContext.Out.WriteLine($"Trace saved: {tracePath}");
            }
            catch (Exception ex)
            {
                TestContext.Out.WriteLine($"Failed to capture artifacts: {ex.Message}");
            }
        }
        else if (Context != null)
        {
            try
            {
                await Context.Tracing.StopAsync();
            }
            catch (Exception ex)
            {
                TestContext.Out.WriteLine($"Warning: Failed to stop tracing: {ex.Message}");
            }
        }

        if (Page != null) await Page.CloseAsync();
        if (Context != null) await Context.CloseAsync();
        if (Browser != null) await Browser.CloseAsync();
    }

    [OneTimeTearDown]
    public virtual async Task OneTimeTearDown()
    {
        await Task.CompletedTask;
    }

    private async Task TruncateAllTablesAsync()
    {
        using var scope = SharedFactory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tables = await dbContext.Database.SqlQueryRaw<string>(@"
            SELECT tablename FROM pg_tables
            WHERE schemaname = 'public'
            AND tablename NOT LIKE 'qrtz_%'
            AND tablename != '__EFMigrationsHistory'
        ").ToListAsync();

        if (tables.Count == 0)
        {
            throw new InvalidOperationException(
                "No application tables found in database. " +
                "This likely indicates database migrations have not run.");
        }

        var invalidTables = tables.Where(t => !ValidTableNamePattern.IsMatch(t)).ToList();
        if (invalidTables.Count > 0)
        {
            throw new InvalidOperationException(
                $"Invalid table names from pg_tables: {string.Join(", ", invalidTables)}");
        }

        var tableList = string.Join(", ", tables.Select(t => $"\"{t}\""));

#pragma warning disable EF1002
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
    /// Asserts that the current URL matches the expected path using Playwright's auto-retry.
    /// </summary>
    protected async Task AssertUrlAsync(string expectedPath, int timeoutMs = 10000)
    {
        var path = expectedPath.StartsWith("/") ? expectedPath : $"/{expectedPath}";

        await Expect(Page).ToHaveURLAsync(
            new Regex($".*{Regex.Escape(path)}(\\?.*)?$"),
            new() { Timeout = timeoutMs });
    }
}
