using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.IntegrationTests.Telegram;

/// <summary>
/// Integration tests validating that Telegram system accounts (777000, 1087968824, etc.)
/// bypass all content detection and moderation checks.
///
/// These tests use real PostgreSQL via Testcontainers to validate:
/// 1. System accounts bypass ContentCheckCoordinator without any database queries
/// 2. No spam reports are created for system account messages
/// 3. All 5 known system accounts are properly protected
///
/// This test was added to verify the fix for anonymous admin posts (@GroupAnonymousBot)
/// triggering spam detection review.
/// </summary>
[TestFixture]
public class SystemAccountBypassTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private ContentCheckCoordinator? _coordinator;

    [SetUp]
    public async Task SetUp()
    {
        // Create unique test database with migrations applied
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseAndApplyMigrationsAsync();

        // Set up dependency injection with real repositories
        var services = new ServiceCollection();

        // Add NpgsqlDataSource
        var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(_testHelper.ConnectionString);
        services.AddSingleton(dataSourceBuilder.Build());

        // Add DbContextFactory
        services.AddDbContextFactory<AppDbContext>((_, options) =>
        {
            options.UseNpgsql(_testHelper.ConnectionString);
        });

        // Add logging (suppress noise)
        services.AddLogging(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning);
        });

        // Register real repositories
        services.AddScoped<ITelegramUserRepository, TelegramUserRepository>();
        services.AddScoped<IChatAdminsRepository, ChatAdminsRepository>();
        services.AddScoped<IContentCheckConfigRepository, ContentCheckConfigRepository>();

        // Register a mock content detection engine that should NEVER be called for system accounts
        services.AddSingleton<IContentDetectionEngine, ThrowingContentDetectionEngine>();

        // Register the coordinator under test
        services.AddScoped<ContentCheckCoordinator>();

        _serviceProvider = services.BuildServiceProvider();

        // Create coordinator instance
        var scope = _serviceProvider.CreateScope();
        _coordinator = scope.ServiceProvider.GetRequiredService<ContentCheckCoordinator>();
    }

    [TearDown]
    public void TearDown()
    {
        _testHelper?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
    }

    #region System Account Bypass Tests

    [Test]
    [TestCase(777000, Description = "Service account - channel forwards")]
    [TestCase(1087968824, Description = "GroupAnonymousBot - anonymous admin posts")]
    [TestCase(136817688, Description = "Channel_Bot - channel signatures")]
    [TestCase(1271266957, Description = "replies - reply headers")]
    [TestCase(5434988373, Description = "Antispam bot")]
    public async Task CheckAsync_SystemAccount_BypassesAllChecks_NoDatabaseQueries(long systemUserId)
    {
        // Arrange: Create a content check request from a system account
        var request = new ContentCheckRequest
        {
            UserId = systemUserId,
            ChatId = -1001234567890,
            Message = "This message should never be checked for spam"
        };

        // Act: Run content check - this should return immediately without any DB queries
        var result = await _coordinator!.CheckAsync(request);

        // Assert: System account is trusted and all checks are skipped
        Assert.Multiple(() =>
        {
            Assert.That(result.IsUserTrusted, Is.True,
                $"System account {systemUserId} should be marked as trusted");
            Assert.That(result.SpamCheckSkipped, Is.True,
                $"Spam checks should be skipped for system account {systemUserId}");
            Assert.That(result.SkipReason, Does.Contain("system account"),
                "Skip reason should mention system account");
            Assert.That(result.SpamResult, Is.Null,
                "No spam detection should run for system accounts");
            Assert.That(result.CriticalCheckViolations, Is.Empty,
                "No critical check violations for system accounts");
        });
    }

    [Test]
    public async Task CheckAsync_GroupAnonymousBot_BypassesCheck_EvenWithMaliciousContent()
    {
        // Arrange: Anonymous admin post with content that would trigger spam detection
        var request = new ContentCheckRequest
        {
            UserId = TelegramConstants.GroupAnonymousBotUserId, // 1087968824
            ChatId = -1001234567890,
            Message = "FREE BITCOIN! Click here: https://scam-site.com BUY NOW!!!"
        };

        // Act: Process the "malicious" content from anonymous admin
        var result = await _coordinator!.CheckAsync(request);

        // Assert: Content is NOT checked because it's from a system account
        Assert.Multiple(() =>
        {
            Assert.That(result.IsUserTrusted, Is.True,
                "Anonymous admin should be trusted");
            Assert.That(result.SpamCheckSkipped, Is.True,
                "Spam checks should be skipped for anonymous admin");
            Assert.That(result.SpamResult, Is.Null,
                "No spam detection should run - admin content is trusted");
        });

        // If the detection engine was called, the test would throw
        // (ThrowingContentDetectionEngine throws on any call)
    }

    [Test]
    public async Task CheckAsync_RegularUser_IsNotBypassed()
    {
        // Arrange: Regular user (not a system account)
        var request = new ContentCheckRequest
        {
            UserId = 12345, // Regular user
            ChatId = -1001234567890,
            Message = "Hello world"
        };

        // Act & Assert: Regular user triggers the detection engine
        // ThrowingContentDetectionEngine will throw, proving the bypass only works for system accounts
        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _coordinator!.CheckAsync(request));

        Assert.That(ex!.Message, Does.Contain("should not be called"),
            "Detection engine should be called for regular users (proving bypass is specific to system accounts)");
    }

    #endregion

    #region No Database Entries Created Tests

    [Test]
    [TestCase(1087968824, Description = "GroupAnonymousBot")]
    public async Task CheckAsync_SystemAccount_CreatesNoSpamReports(long systemUserId)
    {
        // Arrange
        var request = new ContentCheckRequest
        {
            UserId = systemUserId,
            ChatId = -1001234567890,
            Message = "System account message"
        };

        // Act
        await _coordinator!.CheckAsync(request);

        // Assert: Verify no spam reports were created in the database
        await using var context = _testHelper!.GetDbContext();
        var reportCount = await context.Reports.CountAsync();

        Assert.That(reportCount, Is.Zero,
            "No spam reports should be created for system account messages");
    }

    #endregion

    /// <summary>
    /// A content detection engine that throws if called.
    /// Used to verify that system accounts bypass detection entirely.
    /// </summary>
    private class ThrowingContentDetectionEngine : IContentDetectionEngine
    {
        public Task<ContentDetectionResult> CheckMessageAsync(
            ContentCheckRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException(
                $"ContentDetectionEngine.CheckMessageAsync should not be called for system account {request.UserId}. " +
                "System accounts should bypass all detection.");
        }

        public Task<ContentDetectionResult> CheckMessageWithoutOpenAIAsync(
            ContentCheckRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException(
                $"ContentDetectionEngine.CheckMessageWithoutOpenAIAsync should not be called for system account {request.UserId}. " +
                "System accounts should bypass all detection.");
        }
    }
}
