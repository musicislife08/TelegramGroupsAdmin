using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services.AI;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Services.Email;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using TelegramGroupsAdmin.Core.BackgroundJobs;

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

    // NSubstitute mocks for external services
    private readonly IAITranslationService _mockAITranslationService;
    private readonly IFileScannerService _mockFileScannerService;
    private readonly ICloudScannerService _mockCloudScannerService;

    // Telegram service test implementations (pure NSubstitute - no custom test classes needed)
    private readonly ITelegramConfigLoader _mockTelegramConfigLoader;
    private readonly ITelegramBotClient _mockTelegramBotClient;
    private readonly ITelegramBotClientFactory _mockTelegramBotClientFactory;
    private readonly IBotMessageService _mockBotMessageService;
    private readonly IBotChatService _mockBotChatService;
    private readonly IBotDmService _mockBotDmService;
    private readonly IBotModerationService _mockBotModerationService;

    // Job scheduler mock (for verifying timeout cancellation in exam flow tests)
    private readonly IJobScheduler _mockJobScheduler;

    // Exam evaluation mock (for controlling AI responses in exam flow tests)
    private readonly IExamEvaluationService _mockExamEvaluationService;

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

        // Mock ITelegramBotClient for low-level API access (used by some services directly)
        _mockTelegramBotClient = Substitute.For<ITelegramBotClient>();

        // Mock IBotChatService for chat information
        _mockBotChatService = Substitute.For<IBotChatService>();

        // Configure GetChatAsync to return valid ChatFullInfo for any chat
        _mockBotChatService.GetChatAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var chatId = callInfo.Arg<long>();
                return new ChatFullInfo
                {
                    Id = chatId,
                    Type = ChatType.Supergroup,
                    Title = "Test Group",
                    AccentColorId = 0
                };
            });

        // Mock IBotDmService for DM operations (exam flow, notifications, etc.)
        _mockBotDmService = Substitute.For<IBotDmService>();

        // Configure SendDmAsync to return success
        _mockBotDmService.SendDmAsync(
                Arg.Any<long>(), Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new DmDeliveryResult { DmSent = true, Failed = false, MessageId = Random.Shared.Next(1, 100000) });

        // Configure SendDmWithKeyboardAsync to return success with message ID (used for exam questions)
        _mockBotDmService.SendDmWithKeyboardAsync(
                Arg.Any<long>(), Arg.Any<string>(), Arg.Any<global::Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup>(), Arg.Any<CancellationToken>())
            .Returns(new DmDeliveryResult { DmSent = true, Failed = false, MessageId = Random.Shared.Next(1, 100000) });

        // Configure DeleteDmMessageAsync to succeed (used after answering exam questions)
        _mockBotDmService.DeleteDmMessageAsync(
                Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Mock IBotMessageService for message operations
        _mockBotMessageService = Substitute.For<IBotMessageService>();

        // Configure SendAndSaveMessageAsync to return valid Message
        _mockBotMessageService.SendAndSaveMessageAsync(
                Arg.Any<long>(), Arg.Any<string>(), Arg.Any<ParseMode?>(),
                Arg.Any<ReplyParameters?>(), Arg.Any<InlineKeyboardMarkup?>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => new Message
            {
                Id = Random.Shared.Next(1, 100000),
                Date = DateTime.UtcNow,
                Chat = new Chat { Id = callInfo.Arg<long>(), Type = ChatType.Private }
            });

        // Configure EditAndUpdateMessageAsync to return valid Message
        _mockBotMessageService.EditAndUpdateMessageAsync(
                Arg.Any<long>(), Arg.Any<int>(), Arg.Any<string>(),
                Arg.Any<ParseMode?>(), Arg.Any<InlineKeyboardMarkup?>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => new Message
            {
                Id = callInfo.ArgAt<int>(1),
                Date = DateTime.UtcNow,
                Chat = new Chat { Id = callInfo.Arg<long>(), Type = ChatType.Private }
            });

        // Configure AnswerCallbackAsync (used after processing exam callbacks)
        _mockBotMessageService.AnswerCallbackAsync(
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Configure DeleteAndMarkMessageAsync (used to delete exam question messages)
        _mockBotMessageService.DeleteAndMarkMessageAsync(
                Arg.Any<long>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Mock ITelegramBotClientFactory that returns the mock client
        _mockTelegramBotClientFactory = Substitute.For<ITelegramBotClientFactory>();
        _mockTelegramBotClientFactory.GetBotClientAsync()
            .Returns(Task.FromResult(_mockTelegramBotClient));

        // Mock IBotModerationService - returns success for all moderation actions
        // (E2E tests don't have a real Telegram bot, so we mock the service)
        _mockBotModerationService = Substitute.For<IBotModerationService>();
        var successResult = new ModerationResult { Success = true, ChatsAffected = 1 };
        _mockBotModerationService.RestoreUserPermissionsAsync(
                Arg.Any<RestorePermissionsIntent>(), Arg.Any<CancellationToken>())
            .Returns(successResult);
        _mockBotModerationService.KickUserFromChatAsync(
                Arg.Any<KickIntent>(), Arg.Any<CancellationToken>())
            .Returns(successResult);
        _mockBotModerationService.BanUserAsync(
                Arg.Any<BanIntent>(), Arg.Any<CancellationToken>())
            .Returns(successResult);
        _mockBotModerationService.MarkAsSpamAndBanAsync(
                Arg.Any<SpamBanIntent>(), Arg.Any<CancellationToken>())
            .Returns(successResult);
        _mockBotModerationService.WarnUserAsync(
                Arg.Any<WarnIntent>(), Arg.Any<CancellationToken>())
            .Returns(successResult);

        // Mock IJobScheduler - returns predictable job IDs and tracks cancellations
        _mockJobScheduler = Substitute.For<IJobScheduler>();
        _mockJobScheduler.ScheduleJobAsync(
                Arg.Any<string>(), Arg.Any<object>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult($"job-{Guid.NewGuid():N}"));
        _mockJobScheduler.CancelJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));
        _mockJobScheduler.IsScheduledAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        // Mock IExamEvaluationService - default to pass with high confidence
        // Tests can reconfigure via MockExamEvaluation property
        _mockExamEvaluationService = Substitute.For<IExamEvaluationService>();
        _mockExamEvaluationService.IsAvailableAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));
        _mockExamEvaluationService.EvaluateAnswerAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ExamEvaluationResult(Passed: true, Reasoning: "Test passed", Confidence: 0.95));

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
    /// Gets the mock job scheduler. Use to verify job scheduling/cancellation in tests.
    /// </summary>
    public IJobScheduler MockJobScheduler => _mockJobScheduler;

    /// <summary>
    /// Gets the mock exam evaluation service. Configure in tests to control AI pass/fail responses.
    /// </summary>
    public IExamEvaluationService MockExamEvaluation => _mockExamEvaluationService;

    /// <summary>
    /// Gets the mock file scanner service (ClamAV). Configure in tests for specific scan results.
    /// </summary>
    public IFileScannerService MockFileScanner => _mockFileScannerService;

    /// <summary>
    /// Gets the mock cloud scanner service (VirusTotal). Configure in tests for specific scan results.
    /// </summary>
    public ICloudScannerService MockCloudScanner => _mockCloudScannerService;

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

        // Skip ML training (saves 3-5s per factory startup)
        // E2E tests don't need the ML classifier - add WithMlTraining() builder if needed later
        Environment.SetEnvironmentVariable("SKIP_ML_TRAINING", "true");

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

            // Bot services mocks (for exam actions that don't have real Telegram)
            // Must be Scoped to match the original registration
            services.RemoveAll<IBotMessageService>();
            services.AddScoped<IBotMessageService>(_ => _mockBotMessageService);

            services.RemoveAll<IBotChatService>();
            services.AddScoped<IBotChatService>(_ => _mockBotChatService);

            services.RemoveAll<IBotDmService>();
            services.AddScoped<IBotDmService>(_ => _mockBotDmService);

            services.RemoveAll<IBotModerationService>();
            services.AddScoped<IBotModerationService>(_ => _mockBotModerationService);

            // Job scheduler mock (for verifying timeout job cancellation in exam flow tests)
            services.RemoveAll<IJobScheduler>();
            services.AddSingleton(_mockJobScheduler);

            // Exam evaluation mock (Scoped to match real app registration)
            services.RemoveAll<IExamEvaluationService>();
            services.AddScoped<IExamEvaluationService>(_ => _mockExamEvaluationService);
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
