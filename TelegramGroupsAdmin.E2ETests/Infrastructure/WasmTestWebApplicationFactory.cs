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
using TelegramGroupsAdmin.Ui.Server.Services.Email;
using TelegramGroupsAdmin.Telegram.Services;
using WasmProgram = TelegramGroupsAdmin.Ui.Server.Program;

namespace TelegramGroupsAdmin.E2ETests.Infrastructure;

/// <summary>
/// Custom WebApplicationFactory for WASM UI E2E tests.
/// Uses TelegramGroupsAdmin.Ui.Server as the entry point instead of the main Blazor Server app.
/// </summary>
public class WasmTestWebApplicationFactory : WebApplicationFactory<WasmProgram>
{
    private readonly string _databaseName;
    private readonly WasmTestEmailService _testEmailService;
    private string? _tempDataPath;
    private bool _databaseCreated;

    // NSubstitute mocks for external services
    private readonly IAITranslationService _mockAITranslationService;
    private readonly IFileScannerService _mockFileScannerService;
    private readonly ICloudScannerService _mockCloudScannerService;

    // Telegram service test implementations
    private readonly ITelegramConfigLoader _mockTelegramConfigLoader;
    private readonly ITelegramBotClient _mockTelegramBotClient;
    private readonly ITelegramBotClientFactory _mockTelegramBotClientFactory;

    public WasmTestWebApplicationFactory(string? databaseName = null)
    {
        _databaseName = databaseName ?? E2EFixture.GetUniqueDatabaseName();
        _testEmailService = new WasmTestEmailService();

        // Configure NSubstitute mocks with safe defaults
        _mockAITranslationService = Substitute.For<IAITranslationService>();
        _mockAITranslationService.TranslateToEnglishAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((TranslationResult?)null);

        _mockFileScannerService = Substitute.For<IFileScannerService>();
        _mockFileScannerService.ScannerName.Returns("MockClamAV");
        _mockFileScannerService.ScanFileAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new FileScanResult { Scanner = "MockClamAV", IsClean = true, ResultType = ScanResultType.Clean });

        _mockCloudScannerService = Substitute.For<ICloudScannerService>();
        _mockCloudScannerService.ServiceName.Returns("MockVirusTotal");
        _mockCloudScannerService.IsEnabled.Returns(false);
        _mockCloudScannerService.IsQuotaAvailableAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        // Configure Telegram test implementations
        _mockTelegramConfigLoader = Substitute.For<ITelegramConfigLoader>();
        _mockTelegramConfigLoader.LoadConfigAsync().Returns(Task.FromResult("test-bot-token-for-e2e-tests"));

        _mockTelegramBotClient = Substitute.For<ITelegramBotClient>();

        _mockTelegramBotClientFactory = Substitute.For<ITelegramBotClientFactory>();
        _mockTelegramBotClientFactory.GetBotClientAsync()
            .Returns(Task.FromResult(_mockTelegramBotClient));
        _mockTelegramBotClientFactory.GetOperationsAsync()
            .Returns(_ => Task.FromResult<ITelegramOperations>(
                new TelegramOperations(_mockTelegramBotClient, NullLogger<TelegramOperations>.Instance)));

        // Configure to use Kestrel with dynamic port
        UseKestrel(0);
    }

    /// <summary>
    /// Gets the test email service for verifying sent emails in tests.
    /// </summary>
    public WasmTestEmailService EmailService => _testEmailService;

    /// <summary>
    /// Gets the connection string for this test's isolated database.
    /// </summary>
    public string ConnectionString => BuildConnectionString(_databaseName);

    /// <summary>
    /// Gets the base URL where the Kestrel server is listening.
    /// </summary>
    public string ServerAddress => ClientOptions.BaseAddress.ToString().TrimEnd('/');

    /// <summary>
    /// Enables email verification by seeding SendGrid config in the database.
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

        // Use Development environment
        builder.UseEnvironment("Development");

        // Point data path to temp directory
        builder.UseSetting("App:DataPath", _tempDataPath);

        // Point to test database
        builder.UseSetting("ConnectionStrings:PostgreSQL", ConnectionString);

        builder.ConfigureServices(services =>
        {
            // Remove ALL existing DbContext registrations
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

            // Re-register with test database
            services.AddDbContext<AppDbContext>(
                options => options.UseNpgsql(ConnectionString),
                contextLifetime: ServiceLifetime.Scoped,
                optionsLifetime: ServiceLifetime.Singleton);

            services.AddPooledDbContextFactory<AppDbContext>(options =>
                options.UseNpgsql(ConnectionString));

            // Replace email service with test fake
            services.RemoveAll<IEmailService>();
            services.AddSingleton(_testEmailService);
            services.AddScoped<IEmailService>(sp => sp.GetRequiredService<WasmTestEmailService>());

            // Replace external services with NSubstitute mocks
            services.RemoveAll<IAITranslationService>();
            services.AddSingleton(_mockAITranslationService);

            services.RemoveAll<IFileScannerService>();
            services.AddSingleton(_mockFileScannerService);

            services.RemoveAll<ICloudScannerService>();
            services.AddSingleton(_mockCloudScannerService);

            // Telegram services
            services.RemoveAll<ITelegramConfigLoader>();
            services.AddSingleton(_mockTelegramConfigLoader);

            services.RemoveAll<ITelegramBotClientFactory>();
            services.AddSingleton(_mockTelegramBotClientFactory);
        });
    }

    private void EnsureDatabaseCreated()
    {
        if (_databaseCreated) return;

        _tempDataPath = Path.Combine(Path.GetTempPath(), "e2e_tests_wasm", _databaseName);
        Directory.CreateDirectory(_tempDataPath);

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
            try
            {
                using var connection = new NpgsqlConnection(E2EFixture.BaseConnectionString);
                connection.Open();

                using var terminateCmd = connection.CreateCommand();
                terminateCmd.CommandText = $@"
                    SELECT pg_terminate_backend(pid)
                    FROM pg_stat_activity
                    WHERE datname = '{_databaseName}' AND pid <> pg_backend_pid()";
                terminateCmd.ExecuteNonQuery();

                using var dropCmd = connection.CreateCommand();
                dropCmd.CommandText = $"DROP DATABASE IF EXISTS \"{_databaseName}\"";
                dropCmd.ExecuteNonQuery();
            }
            catch
            {
                // Ignore cleanup errors
            }

            try
            {
                if (_tempDataPath != null && Directory.Exists(_tempDataPath))
                {
                    Directory.Delete(_tempDataPath, recursive: true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        base.Dispose(disposing);
    }
}
