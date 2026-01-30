using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestData;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.IntegrationTests.Telegram.Services;

/// <summary>
/// Integration tests for BanCelebrationService - coordinates GIF sending on bans.
///
/// Architecture:
/// - Service checks per-chat config (enabled, trigger types)
/// - Selects random GIF and caption from repositories
/// - Sends to chat via Telegram API (mocked)
/// - Optionally sends DM to banned user (mocked)
/// - Caches Telegram file_id after first send
///
/// Test Strategy:
/// - Real PostgreSQL for config, GIFs, captions, and ban counts
/// - Mocked ITelegramBotClientFactory and IDmDeliveryService (external APIs)
/// - Tests config logic, placeholder replacement, and file_id caching
/// </summary>
[TestFixture]
public class BanCelebrationServiceTests
{
    private const long TestChatId = -100123456789L;
    private const long TestUserId = 12345L;
    private const string TestChatName = "Test Group";
    private const string TestUserName = "SpammerX";

    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private IBanCelebrationService? _service;
    private IBanCelebrationGifRepository? _gifRepository;
    private IBanCelebrationCaptionRepository? _captionRepository;
    private IConfigService? _configService;
    private ITelegramBotClientFactory? _mockBotClientFactory;
    private ITelegramOperations? _mockTelegramOps;
    private IDmDeliveryService? _mockDmDeliveryService;
    private string _tempMediaPath = null!;

    [SetUp]
    public async Task SetUp()
    {
        // Create unique test database with migrations applied
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseAndApplyMigrationsAsync();

        // Seed only telegram users (needed for user_actions FK constraint in SeedBanActions)
        await GoldenDataset.LoadSqlScriptAsync(
            "SQL.00_base_telegram_users.sql",
            sql => _testHelper.ExecuteSqlAsync(sql));

        // Create temp directory for media files
        _tempMediaPath = Path.Combine(Path.GetTempPath(), $"BanCelebrationServiceTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempMediaPath);

        // Set up mocks for external services
        _mockBotClientFactory = Substitute.For<ITelegramBotClientFactory>();
        _mockTelegramOps = Substitute.For<ITelegramOperations>();
        _mockDmDeliveryService = Substitute.For<IDmDeliveryService>();

        // Configure mock to return operations
        _mockBotClientFactory.GetOperationsAsync()
            .Returns(Task.FromResult(_mockTelegramOps)!);

        // Configure mock to return a message with animation
        _mockTelegramOps.SendAnimationAsync(
            Arg.Any<long>(),
            Arg.Any<InputFile>(),
            Arg.Any<string?>(),
            Arg.Any<ParseMode?>(),
            Arg.Any<CancellationToken>()
        ).Returns(callInfo =>
        {
            var msg = TelegramTestFactory.CreateMessage(
                messageId: 999,
                chatId: callInfo.ArgAt<long>(0));
            msg.Animation = new Animation { FileId = "AgACAgIAAxkBAAI_test_file_id_123" };
            return msg;
        });

        // Set up dependency injection
        var services = new ServiceCollection();

        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseNpgsql(_testHelper.ConnectionString));

        services.AddLogging(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

        // Configure MessageHistoryOptions with temp path
        services.Configure<MessageHistoryOptions>(opt =>
            opt.ImageStoragePath = _tempMediaPath);

        // Add HttpClientFactory for URL downloads
        services.AddHttpClient();

        // Mock IVideoFrameExtractionService (not needed for these tests)
        var mockVideoService = Substitute.For<IVideoFrameExtractionService>();
        mockVideoService.IsAvailable.Returns(false);
        services.AddSingleton(mockVideoService);

        // Register repositories (real implementations)
        services.AddScoped<IBanCelebrationGifRepository, BanCelebrationGifRepository>();
        services.AddScoped<IBanCelebrationCaptionRepository, BanCelebrationCaptionRepository>();
        services.AddScoped<IUserActionsRepository, UserActionsRepository>();

        // Register ConfigService and its dependencies (real implementations)
        services.AddScoped<IConfigRepository, ConfigRepository>();
        services.AddScoped<IContentDetectionConfigRepository, ContentDetectionConfigRepository>();
        services.AddHybridCache();
        services.AddDataProtection();
        services.AddScoped<IConfigService, ConfigService>();

        // Register mocked external services
        services.AddSingleton(_mockBotClientFactory);
        services.AddSingleton(_mockDmDeliveryService);

        // Register BanCelebrationService
        services.AddScoped<IBanCelebrationService, BanCelebrationService>();

        _serviceProvider = services.BuildServiceProvider();

        var scope = _serviceProvider.CreateScope();
        _service = scope.ServiceProvider.GetRequiredService<IBanCelebrationService>();
        _gifRepository = scope.ServiceProvider.GetRequiredService<IBanCelebrationGifRepository>();
        _captionRepository = scope.ServiceProvider.GetRequiredService<IBanCelebrationCaptionRepository>();
        _configService = scope.ServiceProvider.GetRequiredService<IConfigService>();
    }

    [TearDown]
    public void TearDown()
    {
        (_serviceProvider as IDisposable)?.Dispose();
        (_mockBotClientFactory as IDisposable)?.Dispose();
        (_mockTelegramOps as IDisposable)?.Dispose();
        (_mockDmDeliveryService as IDisposable)?.Dispose();
        _testHelper?.Dispose();

        // Clean up temp directory
        if (Directory.Exists(_tempMediaPath))
        {
            try
            {
                Directory.Delete(_tempMediaPath, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    #region Config Check Tests

    [Test]
    public async Task SendBanCelebrationAsync_WhenDisabled_ReturnsFalse()
    {
        // Arrange - Add GIF and caption, but leave config disabled (default)
        await SeedTestGifAndCaption();

        // Act
        var result = await _service!.SendBanCelebrationAsync(
            TestChatId, TestChatName, TestUserId, TestUserName, isAutoBan: true);

        // Assert
        Assert.That(result, Is.False);
        await _mockTelegramOps!.DidNotReceive().SendAnimationAsync(
            Arg.Any<long>(), Arg.Any<InputFile>(), Arg.Any<string?>(),
            Arg.Any<ParseMode?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendBanCelebrationAsync_WhenEnabled_SendsGif()
    {
        // Arrange
        await SeedTestGifAndCaption();
        await EnableBanCelebration(TestChatId);

        // Act
        var result = await _service!.SendBanCelebrationAsync(
            TestChatId, TestChatName, TestUserId, TestUserName, isAutoBan: true);

        // Assert
        Assert.That(result, Is.True);
        await _mockTelegramOps!.Received(1).SendAnimationAsync(
            TestChatId, Arg.Any<InputFile>(), Arg.Any<string?>(),
            ParseMode.Markdown, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendBanCelebrationAsync_AutoBanDisabled_SkipsForAutoBan()
    {
        // Arrange
        await SeedTestGifAndCaption();
        await EnableBanCelebration(TestChatId, triggerOnAutoBan: false, triggerOnManualBan: true);

        // Act
        var result = await _service!.SendBanCelebrationAsync(
            TestChatId, TestChatName, TestUserId, TestUserName, isAutoBan: true);

        // Assert
        Assert.That(result, Is.False);
        await _mockTelegramOps!.DidNotReceive().SendAnimationAsync(
            Arg.Any<long>(), Arg.Any<InputFile>(), Arg.Any<string?>(),
            Arg.Any<ParseMode?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendBanCelebrationAsync_ManualBanDisabled_SkipsForManualBan()
    {
        // Arrange
        await SeedTestGifAndCaption();
        await EnableBanCelebration(TestChatId, triggerOnAutoBan: true, triggerOnManualBan: false);

        // Act
        var result = await _service!.SendBanCelebrationAsync(
            TestChatId, TestChatName, TestUserId, TestUserName, isAutoBan: false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task SendBanCelebrationAsync_ManualBanEnabled_SendsForManualBan()
    {
        // Arrange
        await SeedTestGifAndCaption();
        await EnableBanCelebration(TestChatId, triggerOnAutoBan: false, triggerOnManualBan: true);

        // Act
        var result = await _service!.SendBanCelebrationAsync(
            TestChatId, TestChatName, TestUserId, TestUserName, isAutoBan: false);

        // Assert
        Assert.That(result, Is.True);
    }

    #endregion

    #region Library Check Tests

    [Test]
    public async Task SendBanCelebrationAsync_NoGifs_ReturnsFalse()
    {
        // Arrange - Enable feature but don't add any GIFs
        await _captionRepository!.AddAsync("Test caption {username}", "DM caption", "Test");
        await EnableBanCelebration(TestChatId);

        // Act
        var result = await _service!.SendBanCelebrationAsync(
            TestChatId, TestChatName, TestUserId, TestUserName, isAutoBan: true);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task SendBanCelebrationAsync_NoCaptions_ReturnsFalse()
    {
        // Arrange - Enable feature but don't add any captions
        using var gifStream = CreateTestGifStream();
        await _gifRepository!.AddFromFileAsync(gifStream, "test.gif", "Test GIF");
        await EnableBanCelebration(TestChatId);

        // Act
        var result = await _service!.SendBanCelebrationAsync(
            TestChatId, TestChatName, TestUserId, TestUserName, isAutoBan: true);

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region Placeholder Replacement Tests

    [Test]
    public async Task SendBanCelebrationAsync_ReplacesUsernamePlaceholder()
    {
        // Arrange
        using var gifStream = CreateTestGifStream();
        await _gifRepository!.AddFromFileAsync(gifStream, "test.gif", "Test GIF");
        await _captionRepository!.AddAsync("Goodbye {username}!", "DM", "Test");
        await EnableBanCelebration(TestChatId);

        string? capturedCaption = null;
        _mockTelegramOps!.SendAnimationAsync(
            Arg.Any<long>(), Arg.Any<InputFile>(), Arg.Do<string?>(c => capturedCaption = c),
            Arg.Any<ParseMode?>(), Arg.Any<CancellationToken>()
        ).Returns(TelegramTestFactory.CreateMessage(messageId: 1, chatId: TestChatId));

        // Act
        await _service!.SendBanCelebrationAsync(
            TestChatId, TestChatName, TestUserId, TestUserName, isAutoBan: true);

        // Assert
        Assert.That(capturedCaption, Does.Contain(TestUserName));
        Assert.That(capturedCaption, Does.Not.Contain("{username}"));
    }

    [Test]
    public async Task SendBanCelebrationAsync_ReplacesChatnameePlaceholder()
    {
        // Arrange
        using var gifStream = CreateTestGifStream();
        await _gifRepository!.AddFromFileAsync(gifStream, "test.gif", "Test GIF");
        await _captionRepository!.AddAsync("Banned from {chatname}!", "DM", "Test");
        await EnableBanCelebration(TestChatId);

        string? capturedCaption = null;
        _mockTelegramOps!.SendAnimationAsync(
            Arg.Any<long>(), Arg.Any<InputFile>(), Arg.Do<string?>(c => capturedCaption = c),
            Arg.Any<ParseMode?>(), Arg.Any<CancellationToken>()
        ).Returns(TelegramTestFactory.CreateMessage(messageId: 1, chatId: TestChatId));

        // Act
        await _service!.SendBanCelebrationAsync(
            TestChatId, TestChatName, TestUserId, TestUserName, isAutoBan: true);

        // Assert
        Assert.That(capturedCaption, Does.Contain(TestChatName));
        Assert.That(capturedCaption, Does.Not.Contain("{chatname}"));
    }

    [Test]
    public async Task SendBanCelebrationAsync_ReplacesBancountPlaceholder()
    {
        // Arrange
        using var gifStream = CreateTestGifStream();
        await _gifRepository!.AddFromFileAsync(gifStream, "test.gif", "Test GIF");
        await _captionRepository!.AddAsync("Ban #{bancount} today!", "DM", "Test");
        await EnableBanCelebration(TestChatId);

        // Seed some ban actions for today
        await SeedBanActions(3);

        string? capturedCaption = null;
        _mockTelegramOps!.SendAnimationAsync(
            Arg.Any<long>(), Arg.Any<InputFile>(), Arg.Do<string?>(c => capturedCaption = c),
            Arg.Any<ParseMode?>(), Arg.Any<CancellationToken>()
        ).Returns(TelegramTestFactory.CreateMessage(messageId: 1, chatId: TestChatId));

        // Act
        await _service!.SendBanCelebrationAsync(
            TestChatId, TestChatName, TestUserId, TestUserName, isAutoBan: true);

        // Assert
        Assert.That(capturedCaption, Does.Contain("3"));
        Assert.That(capturedCaption, Does.Not.Contain("{bancount}"));
    }

    #endregion

    #region FileId Caching Tests

    [Test]
    public async Task SendBanCelebrationAsync_CachesFileIdAfterFirstSend()
    {
        // Arrange
        using var gifStream = CreateTestGifStream();
        var gif = await _gifRepository!.AddFromFileAsync(gifStream, "test.gif", "Test GIF");
        await _captionRepository!.AddAsync("Test {username}", "DM", "Test");
        await EnableBanCelebration(TestChatId);

        // Verify no file_id initially
        Assert.That(gif.FileId, Is.Null);

        // Act
        await _service!.SendBanCelebrationAsync(
            TestChatId, TestChatName, TestUserId, TestUserName, isAutoBan: true);

        // Assert - Check that file_id was cached
        var updatedGif = await _gifRepository.GetByIdAsync(gif.Id);
        Assert.That(updatedGif!.FileId, Is.EqualTo("AgACAgIAAxkBAAI_test_file_id_123"));
    }

    #endregion

    #region Error Handling Tests

    [Test]
    public async Task SendBanCelebrationAsync_WhenTelegramFails_ReturnsFalseWithoutThrowing()
    {
        // Arrange
        await SeedTestGifAndCaption();
        await EnableBanCelebration(TestChatId);

        _mockTelegramOps!.SendAnimationAsync(
            Arg.Any<long>(), Arg.Any<InputFile>(), Arg.Any<string?>(),
            Arg.Any<ParseMode?>(), Arg.Any<CancellationToken>()
        ).Returns<Message>(x => throw new Exception("Telegram API error"));

        // Act
        var result = await _service!.SendBanCelebrationAsync(
            TestChatId, TestChatName, TestUserId, TestUserName, isAutoBan: true);

        // Assert - Should fail gracefully
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task SendBanCelebrationAsync_WhenBotClientUnavailable_ReturnsFalse()
    {
        // Arrange
        await SeedTestGifAndCaption();
        await EnableBanCelebration(TestChatId);

        _mockBotClientFactory!.GetOperationsAsync()
            .Returns(Task.FromResult<ITelegramOperations>(null!));

        // Act
        var result = await _service!.SendBanCelebrationAsync(
            TestChatId, TestChatName, TestUserId, TestUserName, isAutoBan: true);

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region DM to Banned User Tests

    [Test]
    public async Task SendBanCelebrationAsync_SendToBannedUserEnabled_AttemptsDmDelivery()
    {
        // Arrange
        await SeedTestGifAndCaption();
        await EnableBanCelebration(TestChatId, sendToBannedUser: true);

        // Enable DM welcome mode (required for DM delivery)
        await EnableDmWelcomeMode(TestChatId);

        _mockDmDeliveryService!.SendDmWithMediaAsync(
            Arg.Any<long>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()
        ).Returns(new DmDeliveryResult { DmSent = true });

        // Act
        await _service!.SendBanCelebrationAsync(
            TestChatId, TestChatName, TestUserId, TestUserName, isAutoBan: true);

        // Assert - DM delivery was attempted
        await _mockDmDeliveryService.Received(1).SendDmWithMediaAsync(
            TestUserId, "ban_celebration", Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendBanCelebrationAsync_SendToBannedUserDisabled_SkipsDmDelivery()
    {
        // Arrange
        await SeedTestGifAndCaption();
        await EnableBanCelebration(TestChatId, sendToBannedUser: false);

        // Act
        await _service!.SendBanCelebrationAsync(
            TestChatId, TestChatName, TestUserId, TestUserName, isAutoBan: true);

        // Assert - DM delivery was NOT attempted
        await _mockDmDeliveryService!.DidNotReceive().SendDmWithMediaAsync(
            Arg.Any<long>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendBanCelebrationAsync_NoDmWelcomeMode_SkipsDmDelivery()
    {
        // Arrange - Enable ban celebration with DM but DON'T enable DM welcome mode
        await SeedTestGifAndCaption();
        await EnableBanCelebration(TestChatId, sendToBannedUser: true);
        // Note: Not calling EnableDmWelcomeMode

        // Act
        await _service!.SendBanCelebrationAsync(
            TestChatId, TestChatName, TestUserId, TestUserName, isAutoBan: true);

        // Assert - DM delivery was NOT attempted (no DM mode enabled)
        await _mockDmDeliveryService!.DidNotReceive().SendDmWithMediaAsync(
            Arg.Any<long>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendBanCelebrationAsync_DmFails_StillReturnsTrue()
    {
        // Arrange
        await SeedTestGifAndCaption();
        await EnableBanCelebration(TestChatId, sendToBannedUser: true);
        await EnableDmWelcomeMode(TestChatId);

        _mockDmDeliveryService!.SendDmWithMediaAsync(
            Arg.Any<long>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()
        ).Returns(new DmDeliveryResult { DmSent = false, Failed = true, ErrorMessage = "User blocked bot" });

        // Act
        var result = await _service!.SendBanCelebrationAsync(
            TestChatId, TestChatName, TestUserId, TestUserName, isAutoBan: true);

        // Assert - Chat message succeeded, DM failure doesn't affect result
        Assert.That(result, Is.True);
    }

    #endregion

    #region Helper Methods

    private async Task SeedTestGifAndCaption()
    {
        using var gifStream = CreateTestGifStream();
        await _gifRepository!.AddFromFileAsync(gifStream, "test.gif", "Test GIF");
        await _captionRepository!.AddAsync("🔨 {username} banned!", "You got banned!", "Test Caption");
    }

    private async Task EnableBanCelebration(
        long chatId,
        bool triggerOnAutoBan = true,
        bool triggerOnManualBan = true,
        bool sendToBannedUser = false)
    {
        var config = new BanCelebrationConfig
        {
            Enabled = true,
            TriggerOnAutoBan = triggerOnAutoBan,
            TriggerOnManualBan = triggerOnManualBan,
            SendToBannedUser = sendToBannedUser
        };

        await _configService!.SaveAsync(ConfigType.BanCelebration, chatId, config);
    }

    private async Task EnableDmWelcomeMode(long chatId)
    {
        var welcomeConfig = new WelcomeConfig
        {
            Enabled = true,
            Mode = WelcomeMode.DmWelcome,
            TimeoutSeconds = 60,
            MainWelcomeMessage = "Welcome!",
            DmChatTeaserMessage = "Check DM",
            AcceptButtonText = "Accept",
            DenyButtonText = "Deny",
            DmButtonText = "Open DM"
        };

        await _configService!.SaveAsync(ConfigType.Welcome, chatId, welcomeConfig);
    }

    private async Task SeedBanActions(int count)
    {
        await using var context = _testHelper!.GetDbContext();

        // Use existing telegram users from GoldenDataset (100001-100007)
        var existingUserIds = new[]
        {
            GoldenDataset.TelegramUsers.User1_TelegramUserId,
            GoldenDataset.TelegramUsers.User2_TelegramUserId,
            GoldenDataset.TelegramUsers.User3_TelegramUserId,
            GoldenDataset.TelegramUsers.User4_TelegramUserId,
            GoldenDataset.TelegramUsers.User5_TelegramUserId,
            GoldenDataset.TelegramUsers.User6_TelegramUserId,
            GoldenDataset.TelegramUsers.User7_TelegramUserId
        };

        for (int i = 0; i < count && i < existingUserIds.Length; i++)
        {
            context.UserActions.Add(new Data.Models.UserActionRecordDto
            {
                TelegramUserId = existingUserIds[i],
                ActionType = Data.Models.UserActionType.Ban,
                IssuedAt = DateTimeOffset.UtcNow,
                Reason = "Test ban"
            });
        }

        await context.SaveChangesAsync();
    }

    private static MemoryStream CreateTestGifStream()
    {
        // Minimal 1x1 transparent GIF
        var gifBytes = new byte[]
        {
            0x47, 0x49, 0x46, 0x38, 0x39, 0x61,
            0x01, 0x00, 0x01, 0x00,
            0x00, 0x00, 0x00,
            0x2C, 0x00, 0x00, 0x00, 0x00,
            0x01, 0x00, 0x01, 0x00,
            0x00, 0x02, 0x01, 0x01, 0x00, 0x00,
            0x3B
        };

        return new MemoryStream(gifBytes);
    }

    #endregion
}
