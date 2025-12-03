using Microsoft.Playwright;
using TelegramGroupsAdmin.E2ETests.Infrastructure;

namespace TelegramGroupsAdmin.E2ETests;

/// <summary>
/// Base class for all E2E tests. Provides browser context, page, and test server setup.
/// Each test gets an isolated database and fresh browser context.
/// </summary>
public abstract class E2ETestBase
{
    protected TestWebApplicationFactory Factory { get; private set; } = null!;
    protected HttpClient Client { get; private set; } = null!;
    protected IBrowser Browser { get; private set; } = null!;
    protected IBrowserContext Context { get; private set; } = null!;
    protected IPage Page { get; private set; } = null!;
    protected string BaseUrl { get; private set; } = null!;

    /// <summary>
    /// Gets the test email service for verifying sent emails.
    /// </summary>
    protected TestEmailService EmailService => Factory.EmailService;

    [SetUp]
    public async Task BaseSetUp()
    {
        // Create a new factory with isolated database for each test
        // UseKestrel(0) is called in the constructor for dynamic port assignment
        Factory = new TestWebApplicationFactory();

        // Start the server explicitly (or access Services to trigger startup)
        Factory.StartServer();

        // Get the dynamically assigned server address
        BaseUrl = Factory.ServerAddress;

        // Create an HttpClient for any HTTP-based assertions
        Client = new HttpClient { BaseAddress = new Uri(BaseUrl) };

        // Launch browser and create context
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

        // Clear any previous emails
        EmailService.Clear();
    }

    [TearDown]
    public async Task BaseTearDown()
    {
        var testName = TestContext.CurrentContext.Test.Name;
        var testStatus = TestContext.CurrentContext.Result.Outcome.Status;
        var isFailed = testStatus == NUnit.Framework.Interfaces.TestStatus.Failed;

        // TODO: Change back to failure-only before merging PR
        // Currently capturing all screenshots to verify CSS/JS loads correctly during development
        // Original condition: isFailed
        var shouldCaptureArtifacts = true;

        if (Page != null && shouldCaptureArtifacts)
        {
            try
            {
                var artifactsDir = Path.Combine(TestContext.CurrentContext.WorkDirectory, "test-artifacts");
                Directory.CreateDirectory(artifactsDir);

                // Clean up old artifacts for this test (keeps only latest run)
                CleanupOldArtifacts(artifactsDir, testName);

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
            try { await Context.Tracing.StopAsync(); } catch { /* ignore */ }
        }

        // Safely close browser resources (may be null if setup failed)
        if (Page != null) await Page.CloseAsync();
        if (Context != null) await Context.CloseAsync();
        if (Browser != null) await Browser.CloseAsync();

        Client?.Dispose();
        if (Factory != null) await Factory.DisposeAsync();
    }

    /// <summary>
    /// Removes old artifacts for this test, keeping only the latest run.
    /// Prevents the artifacts folder from growing unbounded during local development.
    /// </summary>
    private static void CleanupOldArtifacts(string artifactsDir, string testName)
    {
        try
        {
            var oldFiles = Directory.GetFiles(artifactsDir)
                .Where(f => Path.GetFileName(f).StartsWith(testName + "_"))
                .ToList();

            foreach (var file in oldFiles)
            {
                File.Delete(file);
            }
        }
        catch
        {
            // Ignore cleanup failures - not critical
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
    /// Asserts that the current URL matches the expected path.
    /// </summary>
    protected async Task AssertUrlAsync(string expectedPath)
    {
        var currentUrl = Page.Url;
        var expectedUrl = expectedPath.StartsWith("http")
            ? expectedPath
            : $"{BaseUrl}{(expectedPath.StartsWith("/") ? expectedPath : $"/{expectedPath}")}";

        // Allow for query strings
        Assert.That(currentUrl, Does.StartWith(expectedUrl.Split('?')[0]),
            $"Expected URL to start with '{expectedUrl}' but was '{currentUrl}'");
    }
}
