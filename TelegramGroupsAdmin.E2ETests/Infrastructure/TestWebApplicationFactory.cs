using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using Telegram.Bot;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.Services.AI;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Services.Email;
using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.E2ETests.Infrastructure;

/// <summary>
/// Custom WebApplicationFactory for E2E tests using .NET 10's native UseKestrel() support.
///
/// Philosophy: Override ONLY what's necessary for testing. Let the real app run as-is.
/// - Database: Point to isolated test database (required for test isolation)
/// - Email: Capture instead of send (so we don't send real emails)
/// - Everything else: Runs normally (bot inactive without token, jobs run but harmless)
///
/// See: https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.mvc.testing.webapplicationfactory-1.usekestrel
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName;
    private readonly TestEmailService _testEmailService;
    private string? _tempDataPath;
    private bool _databaseCreated;
    private bool _skipMlTraining = true; // Default: skip for speed (saves 3-5s)

    // NSubstitute mocks for external services
    private readonly IAITranslationService _mockAITranslationService;
    private readonly IFileScannerService _mockFileScannerService;
    private readonly ICloudScannerService _mockCloudScannerService;

    // Telegram service test implementations (pure NSubstitute - no custom test classes needed)
    private readonly ITelegramConfigLoader _mockTelegramConfigLoader;
    private readonly ITelegramBotClient _mockTelegramBotClient;
    private readonly ITelegramBotClientFactory _mockTelegramBotClientFactory;

    public TestWebApplicationFactory(string? databaseName = null)
    {
        _databaseName = databaseName ?? E2EFixture.GetUniqueDatabaseName();
        _testEmailService = new TestEmailService();

        // Configure NSubstitute mocks with safe defaults
        _mockAITranslationService = Substitute.For<IAITranslationService>();
        _mockAITranslationService.TranslateToEnglishAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((TranslationResult?)null); // Default: no translation needed

        _mockFileScannerService = Substitute.For<IFileScannerService>();
        _mockFileScannerService.ScannerName.Returns("MockClamAV");
        _mockFileScannerService.ScanFileAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new FileScanResult { Scanner = "MockClamAV", IsClean = true, ResultType = ScanResultType.Clean });

        _mockCloudScannerService = Substitute.For<ICloudScannerService>();
        _mockCloudScannerService.ServiceName.Returns("MockVirusTotal");
        _mockCloudScannerService.IsEnabled.Returns(false); // Disabled by default (no API key in tests)
        _mockCloudScannerService.IsQuotaAvailableAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        // Configure Telegram test implementations using pure NSubstitute
        // Mock ITelegramConfigLoader (returns dummy token)
        _mockTelegramConfigLoader = Substitute.For<ITelegramConfigLoader>();
        _mockTelegramConfigLoader.LoadConfigAsync().Returns(Task.FromResult("test-bot-token-for-e2e-tests"));

        // Mock ITelegramBotClient for low-level API access
        _mockTelegramBotClient = Substitute.For<ITelegramBotClient>();

        // Mock ITelegramBotClientFactory that returns real TelegramOperations wrapping the mock client
        // Note: Using a factory lambda to create fresh instances per call prevents test pollution
        // (though TelegramOperations is stateless, this is the correct pattern for mocks)
        _mockTelegramBotClientFactory = Substitute.For<ITelegramBotClientFactory>();
        _mockTelegramBotClientFactory.GetBotClientAsync()
            .Returns(Task.FromResult(_mockTelegramBotClient));
        _mockTelegramBotClientFactory.GetOperationsAsync()
            .Returns(_ => Task.FromResult<ITelegramOperations>(
                new TelegramOperations(_mockTelegramBotClient, NullLogger<TelegramOperations>.Instance)));

        // Configure to use Kestrel with dynamic port (port 0)
        // This MUST be called before StartServer() or accessing Services
        UseKestrel(0);
    }

    /// <summary>
    /// Gets the test email service for verifying sent emails in tests.
    /// </summary>
    public TestEmailService EmailService => _testEmailService;

    /// <summary>
    /// Gets the mock AI translation service. Configure in tests to return specific translations.
    /// </summary>
    public IAITranslationService MockAITranslation => _mockAITranslationService;

    /// <summary>
    /// Gets the mock file scanner service (ClamAV). Configure in tests for specific scan results.
    /// </summary>
    public IFileScannerService MockFileScanner => _mockFileScannerService;

    /// <summary>
    /// Gets the mock cloud scanner service (VirusTotal). Configure in tests for specific scan results.
    /// </summary>
    public ICloudScannerService MockCloudScanner => _mockCloudScannerService;

    /// <summary>
    /// Enables ML classifier training on startup.
    /// By default, ML training is skipped to save 3-5 seconds per factory startup.
    /// Use this for tests that specifically need the ML classifier (training stats, spam detection tests).
    /// Must be called before StartServer() or accessing Services.
    /// </summary>
    public TestWebApplicationFactory WithMlTraining()
    {
        _skipMlTraining = false;
        return this;
    }

    /// <summary>
    /// Gets the connection string for this test's isolated database.
    /// </summary>
    public string ConnectionString => BuildConnectionString(_databaseName);

    /// <summary>
    /// Gets the base URL where the Kestrel server is listening.
    /// Available after calling StartServer() or accessing Services.
    /// </summary>
    public string ServerAddress => ClientOptions.BaseAddress.ToString().TrimEnd('/');

    /// <summary>
    /// Enables email verification by seeding SendGrid config in the database.
    /// Must be called after StartServer() and before registration tests.
    /// The actual emails are captured by TestEmailService, not sent via SendGrid.
    /// </summary>
    public async Task EnableEmailVerificationAsync()
    {
        using var scope = Services.CreateScope();
        var configRepo = scope.ServiceProvider.GetRequiredService<ISystemConfigRepository>();

        var sendGridConfig = new SendGridConfig
        {
            Enabled = true,
            FromAddress = "test@e2e.local",
            FromName = "E2E Test"
        };

        await configRepo.SaveSendGridConfigAsync(sendGridConfig);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Create the test database before app starts
        EnsureDatabaseCreated();

        // Skip ML training by default (saves 3-5s per factory startup)
        // Tests that need ML can call WithMlTraining() before StartServer()
        // Note: Only set to "true", never unset - avoids race conditions between factories
        if (_skipMlTraining)
        {
            Environment.SetEnvironmentVariable("SKIP_ML_TRAINING", "true");
        }
        // Don't clear the env var when !_skipMlTraining - let Program.cs default behavior handle it
        // This prevents race conditions when multiple factories exist with different settings

        // Use Development environment so UseStaticFiles() serves CSS/JS correctly
        // (MapStaticAssets requires publish-time manifest which doesn't exist in tests)
        builder.UseEnvironment("Development");

        // Point data path to temp directory (for data protection keys, media, etc.)
        builder.UseSetting("App:DataPath", _tempDataPath);

        // Point to test database
        builder.UseSetting("ConnectionStrings:PostgreSQL", ConnectionString);

        builder.ConfigureServices(services =>
        {
            // Remove ALL existing DbContext registrations (including pool and factory)
            // The app registers both AddDbContext and AddPooledDbContextFactory
            var dbContextDescriptors = services
                .Where(d => d.ServiceType.FullName?.Contains("AppDbContext") == true ||
                            d.ServiceType.FullName?.Contains("DbContextOptions") == true ||
                            d.ServiceType.FullName?.Contains("DbContextPool") == true ||
                            d.ServiceType.FullName?.Contains("DbContextFactory") == true)
                .ToList();
            foreach (var descriptor in dbContextDescriptors)
            {
                services.Remove(descriptor);
            }

            // Re-register exactly like the original app does (matching lifetimes)
            // AddDbContext with singleton options (required for factory compatibility)
            services.AddDbContext<AppDbContext>(
                options => options.UseNpgsql(ConnectionString),
                contextLifetime: ServiceLifetime.Scoped,
                optionsLifetime: ServiceLifetime.Singleton);

            // AddPooledDbContextFactory for IDbContextFactory<AppDbContext>
            services.AddPooledDbContextFactory<AppDbContext>(options =>
                options.UseNpgsql(ConnectionString));

            // Replace email service with test fake
            services.RemoveAll<IEmailService>();
            services.AddSingleton(_testEmailService);
            services.AddScoped<IEmailService>(sp => sp.GetRequiredService<TestEmailService>());

            // Replace external services with NSubstitute mocks
            // AI translation service
            services.RemoveAll<IAITranslationService>();
            services.AddSingleton(_mockAITranslationService);

            // File scanner service (ClamAV)
            services.RemoveAll<IFileScannerService>();
            services.AddSingleton(_mockFileScannerService);

            // Cloud scanner service (VirusTotal)
            services.RemoveAll<ICloudScannerService>();
            services.AddSingleton(_mockCloudScannerService);

            // Telegram services - test implementations via pure NSubstitute (no custom test classes)
            services.RemoveAll<ITelegramConfigLoader>();
            services.AddSingleton(_mockTelegramConfigLoader);

            services.RemoveAll<ITelegramBotClientFactory>();
            services.AddSingleton(_mockTelegramBotClientFactory);
        });
    }

    private void EnsureDatabaseCreated()
    {
        if (_databaseCreated) return;

        // Create a temp directory for data protection keys and other files
        _tempDataPath = Path.Combine(Path.GetTempPath(), "e2e_tests", _databaseName);
        Directory.CreateDirectory(_tempDataPath);

        // Create the test database on the shared PostgreSQL container
        // Retry with exponential backoff for transient container startup delays
        // (Testcontainers reports "healthy" before connection pool is fully ready)
        const int maxRetries = 3;
        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                using var connection = new NpgsqlConnection(E2EFixture.BaseConnectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = $"CREATE DATABASE \"{_databaseName}\"";
                cmd.ExecuteNonQuery();

                _databaseCreated = true;
                return;
            }
            catch (NpgsqlException) when (attempt < maxRetries - 1)
            {
                // Exponential backoff: 1s, 2s, 4s
                Thread.Sleep(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
            }
        }
    }

    private static string BuildConnectionString(string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(E2EFixture.BaseConnectionString)
        {
            Database = databaseName
        };
        return builder.ConnectionString;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Drop the test database when done
            try
            {
                using var connection = new NpgsqlConnection(E2EFixture.BaseConnectionString);
                connection.Open();

                // Terminate existing connections
                using var terminateCmd = connection.CreateCommand();
                terminateCmd.CommandText = $@"
                    SELECT pg_terminate_backend(pid)
                    FROM pg_stat_activity
                    WHERE datname = '{_databaseName}' AND pid <> pg_backend_pid()";
                terminateCmd.ExecuteNonQuery();

                // Drop the database
                using var dropCmd = connection.CreateCommand();
                dropCmd.CommandText = $"DROP DATABASE IF EXISTS \"{_databaseName}\"";
                dropCmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                // Log but don't fail - cleanup is best-effort
                Console.WriteLine($"Warning: Database cleanup failed for {_databaseName}: {ex.Message}");
            }

            // Clean up temp directory
            try
            {
                if (_tempDataPath != null && Directory.Exists(_tempDataPath))
                {
                    Directory.Delete(_tempDataPath, recursive: true);
                }
            }
            catch (Exception ex)
            {
                // Log but don't fail - cleanup is best-effort
                Console.WriteLine($"Warning: Temp directory cleanup failed for {_tempDataPath}: {ex.Message}");
            }
        }

        base.Dispose(disposing);
    }
}
