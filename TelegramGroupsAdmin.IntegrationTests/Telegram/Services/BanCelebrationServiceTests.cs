using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestData;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Configuration.Services;

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
/// - Mocked ITelegramBotClientFactory and IBotDmService (external APIs)
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
    private IBotMessageService? _mockMessageService;
    private IBotDmService? _mockDmService;
    private string _tempMediaPath = null!;

    [SetUp]
    public async Task SetUp()
    {
        // This test family is the documented exception to the canonical+reducer+mutator
        // pattern. Every test starts from an empty schema and adds its own scenario via
        // production repositories (captions, GIFs, configs). Canonical's reference data
        // (74 captions + 92 GIFs + per-chat configs with ban_celebration enabled) would
        // contaminate the SUT's RNG-driven caption/GIF selection and break the
        // "no captions" / "no GIFs" / "disabled by default" tests. SeedBanActions below
        // is a direct EF insert as a deliberate exception — the user_actions table has
        // no public production "insert ban action" API for tests to route through.
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromEmptyTemplateAsync();

        // Create temp directory for media files
        _tempMediaPath = Path.Combine(Path.GetTempPath(), $"BanCelebrationServiceTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempMediaPath);

        // Set up mocks for external services
        _mockMessageService = Substitute.For<IBotMessageService>();
        _mockDmService = Substitute.For<IBotDmService>();

        // Configure mock to return a message with animation (entity-based caption overload)
        _mockMessageService.SendAndSaveAnimationAsync(
            Arg.Any<long>(),
            Arg.Any<InputFile>(),
            Arg.Any<TelegramMessage>(),
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

        // Configure AppOptions with temp path
        services.Configure<AppOptions>(opt =>
            opt.DataPath = _tempMediaPath);

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
        services.AddScoped<IProfileScanResultsRepository, ProfileScanResultsRepository>();

        // Register PipelineMetrics (real singleton - records masked-username metric)
        services.AddSingleton<PipelineMetrics>();

        // Register ConfigService and its dependencies (real implementations)
        services.AddScoped<IConfigRepository, ConfigRepository>();
        services.AddScoped<IContentDetectionConfigRepository, ContentDetectionConfigRepository>();
        services.AddHybridCache();
        services.AddDataProtection();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IConfigService, ConfigService>();

        // Register mocked external services
        services.AddSingleton(_mockMessageService);
        services.AddSingleton(_mockDmService);

        // Register BanCelebrationCache (real singleton for shuffle-bag state)
        services.AddSingleton<IBanCelebrationCache, BanCelebrationCache>();

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
            new ChatIdentity(TestChatId, TestChatName), new UserIdentity(TestUserId, TestUserName, null, null), isAutoBan: true);

        // Assert
        Assert.That(result, Is.False);
        await _mockMessageService!.DidNotReceive().SendAndSaveAnimationAsync(
            Arg.Any<long>(), Arg.Any<InputFile>(), Arg.Any<TelegramMessage>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendBanCelebrationAsync_WhenEnabled_SendsGif()
    {
        // Arrange
        await SeedTestGifAndCaption();
        await EnableBanCelebration(TestChatId);

        // Act
        var result = await _service!.SendBanCelebrationAsync(
            new ChatIdentity(TestChatId, TestChatName), new UserIdentity(TestUserId, TestUserName, null, null), isAutoBan: true);

        // Assert
        Assert.That(result, Is.True);
        await _mockMessageService!.Received(1).SendAndSaveAnimationAsync(
            TestChatId, Arg.Any<InputFile>(), Arg.Any<TelegramMessage>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendBanCelebrationAsync_AutoBanDisabled_SkipsForAutoBan()
    {
        // Arrange
        await SeedTestGifAndCaption();
        await EnableBanCelebration(TestChatId, triggerOnAutoBan: false, triggerOnManualBan: true);

        // Act
        var result = await _service!.SendBanCelebrationAsync(
            new ChatIdentity(TestChatId, TestChatName), new UserIdentity(TestUserId, TestUserName, null, null), isAutoBan: true);

        // Assert
        Assert.That(result, Is.False);
        await _mockMessageService!.DidNotReceive().SendAndSaveAnimationAsync(
            Arg.Any<long>(), Arg.Any<InputFile>(), Arg.Any<TelegramMessage>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendBanCelebrationAsync_ManualBanDisabled_SkipsForManualBan()
    {
        // Arrange
        await SeedTestGifAndCaption();
        await EnableBanCelebration(TestChatId, triggerOnAutoBan: true, triggerOnManualBan: false);

        // Act
        var result = await _service!.SendBanCelebrationAsync(
            new ChatIdentity(TestChatId, TestChatName), new UserIdentity(TestUserId, TestUserName, null, null), isAutoBan: false);

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
            new ChatIdentity(TestChatId, TestChatName), new UserIdentity(TestUserId, TestUserName, null, null), isAutoBan: false);

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
            new ChatIdentity(TestChatId, TestChatName), new UserIdentity(TestUserId, TestUserName, null, null), isAutoBan: true);

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
            new ChatIdentity(TestChatId, TestChatName), new UserIdentity(TestUserId, TestUserName, null, null), isAutoBan: true);

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
        _mockMessageService!.SendAndSaveAnimationAsync(
            Arg.Any<long>(), Arg.Any<InputFile>(), Arg.Do<TelegramMessage>(m => capturedCaption = m.Text),
            Arg.Any<CancellationToken>()
        ).Returns(TelegramTestFactory.CreateMessage(messageId: 1, chatId: TestChatId));

        // Act
        await _service!.SendBanCelebrationAsync(
            new ChatIdentity(TestChatId, TestChatName), new UserIdentity(TestUserId, TestUserName, null, null), isAutoBan: true);

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
        _mockMessageService!.SendAndSaveAnimationAsync(
            Arg.Any<long>(), Arg.Any<InputFile>(), Arg.Do<TelegramMessage>(m => capturedCaption = m.Text),
            Arg.Any<CancellationToken>()
        ).Returns(TelegramTestFactory.CreateMessage(messageId: 1, chatId: TestChatId));

        // Act
        await _service!.SendBanCelebrationAsync(
            new ChatIdentity(TestChatId, TestChatName), new UserIdentity(TestUserId, TestUserName, null, null), isAutoBan: true);

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

        // Seed 3 ban actions for today (direct EF insert is this test family's
        // documented exception — see the SetUp docstring for rationale).
        await SeedBanActions(3);

        string? capturedCaption = null;
        _mockMessageService!.SendAndSaveAnimationAsync(
            Arg.Any<long>(), Arg.Any<InputFile>(), Arg.Do<TelegramMessage>(m => capturedCaption = m.Text),
            Arg.Any<CancellationToken>()
        ).Returns(TelegramTestFactory.CreateMessage(messageId: 1, chatId: TestChatId));

        // Act
        await _service!.SendBanCelebrationAsync(
            new ChatIdentity(TestChatId, TestChatName), new UserIdentity(TestUserId, TestUserName, null, null), isAutoBan: true);

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
            new ChatIdentity(TestChatId, TestChatName), new UserIdentity(TestUserId, TestUserName, null, null), isAutoBan: true);

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

        _mockMessageService!.SendAndSaveAnimationAsync(
            Arg.Any<long>(), Arg.Any<InputFile>(), Arg.Any<TelegramMessage>(),
            Arg.Any<CancellationToken>()
        ).Returns<Message>(x => throw new Exception("Telegram API error"));

        // Act
        var result = await _service!.SendBanCelebrationAsync(
            new ChatIdentity(TestChatId, TestChatName), new UserIdentity(TestUserId, TestUserName, null, null), isAutoBan: true);

        // Assert - Should fail gracefully
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

        _mockDmService!.SendDmWithMediaEntitiesAsync(
            Arg.Any<UserIdentity>(), Arg.Any<string>(), Arg.Any<TelegramMessage>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()
        ).Returns(new DmDeliveryResult { DmSent = true });

        // Act
        await _service!.SendBanCelebrationAsync(
            new ChatIdentity(TestChatId, TestChatName), new UserIdentity(TestUserId, TestUserName, null, null), isAutoBan: true);

        // Assert - DM delivery was attempted
        await _mockDmService!.Received(1).SendDmWithMediaEntitiesAsync(
            Arg.Is<UserIdentity>(u => u.Id == TestUserId), "ban_celebration", Arg.Any<TelegramMessage>(),
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
            new ChatIdentity(TestChatId, TestChatName), new UserIdentity(TestUserId, TestUserName, null, null), isAutoBan: true);

        // Assert - DM delivery was NOT attempted
        await _mockDmService!.DidNotReceive().SendDmWithMediaEntitiesAsync(
            Arg.Any<UserIdentity>(), Arg.Any<string>(), Arg.Any<TelegramMessage>(),
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
            new ChatIdentity(TestChatId, TestChatName), new UserIdentity(TestUserId, TestUserName, null, null), isAutoBan: true);

        // Assert - DM delivery was NOT attempted (no DM mode enabled)
        await _mockDmService!.DidNotReceive().SendDmWithMediaEntitiesAsync(
            Arg.Any<UserIdentity>(), Arg.Any<string>(), Arg.Any<TelegramMessage>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendBanCelebrationAsync_DmFails_StillReturnsTrue()
    {
        // Arrange
        await SeedTestGifAndCaption();
        await EnableBanCelebration(TestChatId, sendToBannedUser: true);
        await EnableDmWelcomeMode(TestChatId);

        _mockDmService!.SendDmWithMediaEntitiesAsync(
            Arg.Any<UserIdentity>(), Arg.Any<string>(), Arg.Any<TelegramMessage>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()
        ).Returns(new DmDeliveryResult { DmSent = false, Failed = true, ErrorMessage = "User blocked bot" });

        // Act
        var result = await _service!.SendBanCelebrationAsync(
            new ChatIdentity(TestChatId, TestChatName), new UserIdentity(TestUserId, TestUserName, null, null), isAutoBan: true);

        // Assert - Chat message succeeded, DM failure doesn't affect result
        Assert.That(result, Is.True);
    }

    #endregion

    #region Explicit Username Masking Tests

    [Test]
    public async Task SendBanCelebrationAsync_AiFlaggedAndMaskingOn_MasksUsernameInChatCaption()
    {
        // Arrange: scan row with ExplicitDisplayText=true + per-chat config with masking on.
        await SeedExplicitFlaggedScanAsync(TestUserId, explicitFlag: true);
        await EnableBanCelebration(TestChatId);
        await SetWelcomeProfileScanMaskingAsync(maskingEnabled: true,
            redactionText: "[explicit username redacted]");
        using var gifStream = CreateTestGifStream();
        await _gifRepository!.AddFromFileAsync(gifStream, "test.gif", "Test GIF");
        await _captionRepository!.AddAsync("{username} got banned!", "DM", "Test");

        // Act
        var sent = await _service!.SendBanCelebrationAsync(
            chat: ChatIdentity.FromId(TestChatId),
            bannedUser: new UserIdentity(TestUserId, TestUserName, null, null),
            isAutoBan: true,
            cancellationToken: CancellationToken.None);

        // Assert
        Assert.That(sent, Is.True);
        await _mockMessageService!.Received(1).SendAndSaveAnimationAsync(
            TestChatId,
            Arg.Any<InputFile>(),
            Arg.Is<TelegramMessage>(m => m.Text.Contains("[explicit username redacted]")
                                      && !m.Text.Contains(TestUserName)),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendBanCelebrationAsync_AiFlaggedButMaskingOff_LeavesDisplayNameInCaption()
    {
        // Arrange: scan row flagged, but masking disabled via config.
        await SeedExplicitFlaggedScanAsync(TestUserId, explicitFlag: true);
        await EnableBanCelebration(TestChatId);
        await SetWelcomeProfileScanMaskingAsync(maskingEnabled: false,
            redactionText: "[explicit username redacted]");
        using var gifStream = CreateTestGifStream();
        await _gifRepository!.AddFromFileAsync(gifStream, "test.gif", "Test GIF");
        await _captionRepository!.AddAsync("{username} got banned!", "DM", "Test");

        // Act
        await _service!.SendBanCelebrationAsync(
            chat: ChatIdentity.FromId(TestChatId),
            bannedUser: new UserIdentity(TestUserId, TestUserName, null, null),
            isAutoBan: true,
            cancellationToken: CancellationToken.None);

        // Assert
        await _mockMessageService!.Received(1).SendAndSaveAnimationAsync(
            TestChatId,
            Arg.Any<InputFile>(),
            Arg.Is<TelegramMessage>(m => m.Text.Contains(TestUserName)
                                      && !m.Text.Contains("[explicit username redacted]")),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendBanCelebrationAsync_NoScanRow_LeavesDisplayNameInCaption()
    {
        // Arrange: no scan row seeded - user has never been scanned. Masking is on,
        // so the service consults the scan repo and finds nothing; falls through to
        // the display name.
        await EnableBanCelebration(TestChatId);
        await SetWelcomeProfileScanMaskingAsync(maskingEnabled: true,
            redactionText: "[explicit username redacted]");
        using var gifStream = CreateTestGifStream();
        await _gifRepository!.AddFromFileAsync(gifStream, "test.gif", "Test GIF");
        await _captionRepository!.AddAsync("{username} got banned!", "DM", "Test");

        // Act
        await _service!.SendBanCelebrationAsync(
            chat: ChatIdentity.FromId(TestChatId),
            bannedUser: new UserIdentity(TestUserId, TestUserName, null, null),
            isAutoBan: true,
            cancellationToken: CancellationToken.None);

        // Assert
        await _mockMessageService!.Received(1).SendAndSaveAnimationAsync(
            TestChatId,
            Arg.Any<InputFile>(),
            Arg.Is<TelegramMessage>(m => m.Text.Contains(TestUserName)
                                      && !m.Text.Contains("[explicit username redacted]")),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendBanCelebrationAsync_ProfileScanDisabledButStaleFlaggedScanExists_DoesNotMaskCaption()
    {
        // Arrange: an old flagged scan row exists (from when scanning was on).
        await SeedExplicitFlaggedScanAsync(TestUserId, explicitFlag: true);

        // Admin has since disabled profile scanning entirely. The child masking
        // toggle is still at its default (true) because the UI only disables the
        // child switch under the parent - it doesn't reset its stored value.
        await EnableBanCelebration(TestChatId);
        await SetWelcomeProfileScanMaskingAsync(
            maskingEnabled: true,
            redactionText: "[explicit username redacted]",
            profileScanEnabled: false);
        using var gifStream = CreateTestGifStream();
        await _gifRepository!.AddFromFileAsync(gifStream, "test.gif", "Test GIF");
        await _captionRepository!.AddAsync("{username} got banned!", "DM", "Test");

        // Act
        await _service!.SendBanCelebrationAsync(
            chat: ChatIdentity.FromId(TestChatId),
            bannedUser: new UserIdentity(TestUserId, TestUserName, null, null),
            isAutoBan: true,
            cancellationToken: CancellationToken.None);

        // Assert: caption uses DisplayName, NOT the redaction text - profile scan
        // is the parent kill-switch and overrides the stale child masking value.
        await _mockMessageService!.Received(1).SendAndSaveAnimationAsync(
            TestChatId,
            Arg.Any<InputFile>(),
            Arg.Is<TelegramMessage>(m => m.Text.Contains(TestUserName)
                                      && !m.Text.Contains("[explicit username redacted]")),
            Arg.Any<CancellationToken>());
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

        await _configService!.SaveBanCelebrationAsync(ChatIdentity.FromId(chatId), config, Actor.SystemSeed);
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

        await _configService!.SaveWelcomeAsync(ChatIdentity.FromId(chatId), welcomeConfig, Actor.SystemSeed);
    }

    /// <summary>
    /// Raw INSERT (rare exception): this test class is the documented
    /// canonical-clone exception (see SetUp comment). The masking
    /// scenario requires a profile_scan_results row, but the class
    /// uses empty-template so canonical extension doesn't help.
    /// IProfileScanResultsRepository.InsertAsync is NOT used because
    /// the scan row is prerequisite setup, not the assertion subject.
    /// The telegram_users FK parent is also raw-INSERTed for the same
    /// reason (mirrors SeedBanActions which uses direct EF inserts).
    /// </summary>
    private async Task SeedExplicitFlaggedScanAsync(long userId, bool explicitFlag)
    {
        await using var context = _testHelper!.GetDbContext();

        // Satisfy FK_profile_scan_results_telegram_users_user_id: the
        // scan row references telegram_users.telegram_user_id.
        await context.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO telegram_users (telegram_user_id, is_bot, is_trusted, is_active, is_banned, bot_dm_enabled, first_seen_at, last_seen_at, created_at, updated_at, has_pinned_stories, is_fake, is_scam, is_verified, profile_scan_excluded, kick_count)
               VALUES ({userId}, false, false, true, false, false, NOW(), NOW(), NOW(), NOW(), false, false, false, false, false, 0)
               ON CONFLICT (telegram_user_id) DO NOTHING");

        await context.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO profile_scan_results
                   (user_id, scanned_at, score, outcome, rule_score, ai_score, ai_reason, ai_signals, ai_explicit_display_text)
               VALUES
                   ({userId}, NOW(), 4.5, 2, 0.0, 4.5, 'test', 'test_signal', {explicitFlag})");
    }

    private async Task SetWelcomeProfileScanMaskingAsync(
        bool maskingEnabled,
        string redactionText,
        bool profileScanEnabled = true)
    {
        var welcomeConfig = new WelcomeConfig
        {
            Enabled = true,
            Mode = WelcomeMode.ChatAcceptDeny,
            TimeoutSeconds = 60,
            MainWelcomeMessage = "Welcome!",
            DmChatTeaserMessage = "Check DM",
            AcceptButtonText = "Accept",
            DenyButtonText = "Deny",
            DmButtonText = "Open DM",
            JoinSecurity = new JoinSecurityConfig
            {
                ProfileScan = new ProfileScanConfig
                {
                    Enabled = profileScanEnabled,
                    MaskExplicitUsername = maskingEnabled,
                    ExplicitUsernameRedactionText = redactionText
                }
            }
        };

        await _configService!.SaveWelcomeAsync(ChatIdentity.FromId(TestChatId), welcomeConfig, Actor.SystemSeed);
    }

    private async Task SeedBanActions(int count)
    {
        await using var context = _testHelper!.GetDbContext();

        // Insert telegram_users + user_actions in one transaction-free batch.
        // Self-contained: no dependency on the legacy 00_base_telegram_users.sql
        // (which is on the cleanup-deletion trajectory). Synthetic IDs live in
        // the canonical 9_xxx_xxx_xxx_xxx identity space so they can't collide
        // with any future canonical sample.
        const long SyntheticUserIdBase = 9100000000000L;
        for (int i = 0; i < count; i++)
        {
            var bannedUserId = SyntheticUserIdBase + i;
            context.TelegramUsers.Add(new Data.Models.TelegramUserDto
            {
                TelegramUserId = bannedUserId,
                Username = $"test_bancelebration_user_{i}",
                FirstName = "Test",
            });
            context.UserActions.Add(new Data.Models.UserActionRecordDto
            {
                UserId = bannedUserId,
                SystemIdentifier = "test-bancelebration",
                ActionType = (int)UserActionType.Ban,
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
