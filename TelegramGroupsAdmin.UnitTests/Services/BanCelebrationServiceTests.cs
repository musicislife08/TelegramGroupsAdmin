using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Configuration.Services;

namespace TelegramGroupsAdmin.UnitTests.Services;

/// <summary>
/// Unit tests for BanCelebrationService.
/// Tests config handling, GIF/caption retrieval, placeholder replacement, and file caching.
/// GIF/caption rotation itself is database-backed (ClaimNextForCycleAsync) and is covered by
/// the repository integration tests, so it is mocked directly here rather than re-tested.
/// </summary>
[TestFixture]
public class BanCelebrationServiceTests
{
    private const long TestChatId = 123;
    private const long TestUserId = 456;
    private static readonly ChatIdentity TestChat = new(TestChatId, "Test Chat");
    private static readonly UserIdentity TestBannedUser = new(TestUserId, "Bad", "User", null);

    private IConfigService _mockConfigService = null!;
    private IBanCelebrationGifRepository _mockGifRepository = null!;
    private IBanCelebrationCaptionRepository _mockCaptionRepository = null!;
    private IProfileScanResultsRepository _mockScanRepository = null!;
    private IBotMessageService _mockMessageService = null!;
    private IBotDmService _mockDmService = null!;
    private IUserActionsRepository _mockUserActionsRepository = null!;
    private IOptions<AppOptions> _appOptions = null!;
    private ILogger<BanCelebrationService> _mockLogger = null!;
    private PipelineMetrics _pipelineMetrics = null!;
    private BanCelebrationService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _mockConfigService = Substitute.For<IConfigService>();
        _mockGifRepository = Substitute.For<IBanCelebrationGifRepository>();
        _mockCaptionRepository = Substitute.For<IBanCelebrationCaptionRepository>();
        _mockScanRepository = Substitute.For<IProfileScanResultsRepository>();
        _mockMessageService = Substitute.For<IBotMessageService>();
        _mockDmService = Substitute.For<IBotDmService>();
        _mockUserActionsRepository = Substitute.For<IUserActionsRepository>();
        _mockLogger = Substitute.For<ILogger<BanCelebrationService>>();
        _pipelineMetrics = new PipelineMetrics();

        // Setup AppOptions
        _appOptions = Options.Create(new AppOptions { DataPath = "/data" });

        // Default config setup - enabled celebration
        var defaultConfig = new BanCelebrationConfig
        {
            Enabled = true,
            TriggerOnAutoBan = true,
            TriggerOnManualBan = true,
            SendToBannedUser = false
        };
        _mockConfigService.GetEffectiveBanCelebrationAsync(Arg.Any<long>())
            .Returns(defaultConfig);

        // Default user actions repository returns 0 bans
        _mockUserActionsRepository.GetTodaysBanCountAsync(Arg.Any<CancellationToken>())
            .Returns(0);

        // Default scan repository returns no scan record - masking branch off
        _mockScanRepository.GetLatestByUserIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns((ProfileScanResultRecord?)null);

        _sut = new BanCelebrationService(
            _mockConfigService,
            _mockGifRepository,
            _mockCaptionRepository,
            _mockScanRepository,
            _mockMessageService,
            _mockDmService,
            _mockUserActionsRepository,
            _appOptions,
            _mockLogger,
            _pipelineMetrics);
    }

    /// <summary>
    /// Configures the substituted IConfigService to return a WelcomeConfig with the given
    /// per-chat masking toggle and redaction text. Used by explicit-username-masking tests.
    /// </summary>
    private void EnableProfileScanConfig(bool maskExplicitUsername, string redactionText)
    {
        var welcomeConfig = new WelcomeConfig
        {
            Enabled = true,
            JoinSecurity = new JoinSecurityConfig
            {
                ProfileScan = new ProfileScanConfig
                {
                    Enabled = true,
                    MaskExplicitUsername = maskExplicitUsername,
                    ExplicitUsernameRedactionText = redactionText
                }
            }
        };

        _mockConfigService.GetEffectiveWelcomeAsync(TestChatId, Arg.Any<CancellationToken>())
            .Returns(welcomeConfig);
    }

    /// <summary>
    /// Seeds the GIF and caption repositories with a single GIF (Id=1) and a single
    /// caption (Id=1) whose Text uses the supplied template. Used by explicit-username-masking
    /// tests so the captured caption argument can be asserted against the template's
    /// post-replacement output.
    /// </summary>
    private void SeedOneGifAndOneCaption(string captionTemplate)
    {
        var gif = new BanCelebrationGif { Id = 1, FilePath = "ban-gifs/1.gif", FileId = "file1" };
        var caption = new BanCelebrationCaption { Id = 1, Text = captionTemplate, DmText = "DM" };

        _mockGifRepository.ClaimNextForCycleAsync(Arg.Any<CancellationToken>()).Returns(gif);
        _mockCaptionRepository.ClaimNextForCycleAsync(Arg.Any<CancellationToken>()).Returns(caption);

        SetupSuccessfulSendAnimation();
    }

    /// <summary>
    /// Helper to setup SendAndSaveAnimationAsync (entity overload) to return a Message with Animation.
    /// Uses direct object initialization (Telegram.Bot types have settable properties).
    /// </summary>
    private void SetupSuccessfulSendAnimation(string fileId = "cached_file_id")
    {
        var message = new Message
        {
            Animation = new Animation { FileId = fileId }
        };
        _mockMessageService.SendAndSaveAnimationAsync(
            Arg.Any<long>(),
            Arg.Any<InputFile>(),
            Arg.Any<TelegramMessage>(),
            Arg.Any<CancellationToken>())
            .Returns(message);
    }

    #region GIF/Caption Availability Tests

    [Test]
    public async Task SendBanCelebration_WhenNoGifsExist_ReturnsFalse()
    {
        // Arrange - Repository has no GIF left to claim (library empty)
        _mockGifRepository.ClaimNextForCycleAsync(Arg.Any<CancellationToken>())
            .Returns((BanCelebrationGif?)null);

        // Act
        var result = await _sut.SendBanCelebrationAsync(new ChatIdentity(123, "Chat"), new UserIdentity(456, "User", null, null), true);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task SendBanCelebration_WhenNoCaptionsExist_ReturnsFalse()
    {
        // Arrange - GIF exists but no caption left to claim (library empty)
        _mockGifRepository.ClaimNextForCycleAsync(Arg.Any<CancellationToken>())
            .Returns(new BanCelebrationGif { Id = 1, FilePath = "ban-gifs/1.gif", FileId = "file1" });
        _mockCaptionRepository.ClaimNextForCycleAsync(Arg.Any<CancellationToken>())
            .Returns((BanCelebrationCaption?)null);

        // Act
        var result = await _sut.SendBanCelebrationAsync(new ChatIdentity(123, "Chat"), new UserIdentity(456, "User", null, null), true);

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region Configuration Tests

    [Test]
    public async Task SendBanCelebration_WhenDisabled_ReturnsFalse()
    {
        // Arrange
        var disabledConfig = new BanCelebrationConfig { Enabled = false };
        _mockConfigService.GetEffectiveBanCelebrationAsync(Arg.Any<long>())
            .Returns(disabledConfig);

        // Act
        var result = await _sut.SendBanCelebrationAsync(new ChatIdentity(123, "Chat"), new UserIdentity(456, "User", null, null), true);

        // Assert
        Assert.That(result, Is.False);

        // Verify repository was never consulted (short-circuited at config check)
        await _mockGifRepository.DidNotReceive().ClaimNextForCycleAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendBanCelebration_WhenAutoBanDisabled_AndIsAutoBan_ReturnsFalse()
    {
        // Arrange
        var config = new BanCelebrationConfig
        {
            Enabled = true,
            TriggerOnAutoBan = false,
            TriggerOnManualBan = true
        };
        _mockConfigService.GetEffectiveBanCelebrationAsync(Arg.Any<long>())
            .Returns(config);

        // Act
        var result = await _sut.SendBanCelebrationAsync(new ChatIdentity(123, "Chat"), new UserIdentity(456, "User", null, null), isAutoBan: true);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task SendBanCelebration_WhenManualBanDisabled_AndIsManualBan_ReturnsFalse()
    {
        // Arrange
        var config = new BanCelebrationConfig
        {
            Enabled = true,
            TriggerOnAutoBan = true,
            TriggerOnManualBan = false
        };
        _mockConfigService.GetEffectiveBanCelebrationAsync(Arg.Any<long>())
            .Returns(config);

        // Act
        var result = await _sut.SendBanCelebrationAsync(new ChatIdentity(123, "Chat"), new UserIdentity(456, "User", null, null), isAutoBan: false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task SendBanCelebration_WhenConfigIsNull_UsesDefaultConfigWhichIsDisabled()
    {
        // Arrange - Return null config (service falls back to BanCelebrationConfig.Default)
        _mockConfigService.GetEffectiveBanCelebrationAsync(Arg.Any<long>())
            .Returns((BanCelebrationConfig?)null);

        // Act - Default config has Enabled=false (feature is opt-in)
        var result = await _sut.SendBanCelebrationAsync(new ChatIdentity(123, "Chat"), new UserIdentity(456, "User", null, null), isAutoBan: true);

        // Assert - Should return false because default config has Enabled=false
        Assert.That(result, Is.False);

        // Verify repository was never consulted (short-circuited at config check)
        await _mockGifRepository.DidNotReceive().ClaimNextForCycleAsync(Arg.Any<CancellationToken>());
    }

    #endregion

    #region Placeholder Replacement Tests

    [Test]
    public async Task SendBanCelebration_ReplacesPlaceholdersInCaption()
    {
        // Arrange
        var gif = new BanCelebrationGif { Id = 1, FilePath = "ban-gifs/1.gif", FileId = "file1" };
        var caption = new BanCelebrationCaption
        {
            Id = 1,
            Text = "{username} banned from {chatname}! Ban #{bancount}",
            DmText = "You were banned"
        };

        _mockGifRepository.ClaimNextForCycleAsync(Arg.Any<CancellationToken>()).Returns(gif);
        _mockCaptionRepository.ClaimNextForCycleAsync(Arg.Any<CancellationToken>()).Returns(caption);
        _mockUserActionsRepository.GetTodaysBanCountAsync(Arg.Any<CancellationToken>()).Returns(42);

        SetupSuccessfulSendAnimation();

        // Act
        await _sut.SendBanCelebrationAsync(new ChatIdentity(123, "Test Group"), new UserIdentity(456, "SpammerBob", null, null), true);

        // Assert - Verify the caption sent to Telegram has placeholders replaced
        await _mockMessageService.Received(1).SendAndSaveAnimationAsync(
            123,
            Arg.Any<InputFile>(),
            Arg.Is<TelegramMessage>(m => m!.Text == "SpammerBob banned from Test Group! Ban #42"),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Ban Count Tests

    [Test]
    public async Task SendBanCelebration_WhenBanCountQueryFails_UsesZeroAsFallback()
    {
        // Arrange
        var gif = new BanCelebrationGif { Id = 1, FilePath = "ban-gifs/1.gif", FileId = "file1" };
        var caption = new BanCelebrationCaption
        {
            Id = 1,
            Text = "Ban #{bancount}!",
            DmText = "Banned"
        };

        _mockGifRepository.ClaimNextForCycleAsync(Arg.Any<CancellationToken>()).Returns(gif);
        _mockCaptionRepository.ClaimNextForCycleAsync(Arg.Any<CancellationToken>()).Returns(caption);

        // Ban count query throws
        _mockUserActionsRepository.GetTodaysBanCountAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("DB connection failed"));

        SetupSuccessfulSendAnimation();

        // Act
        var result = await _sut.SendBanCelebrationAsync(new ChatIdentity(123, "Chat"), new UserIdentity(456, "User", null, null), true);

        // Assert - Should still succeed, using 0 as fallback ban count
        Assert.That(result, Is.True);

        // Verify caption used 0 as the ban count fallback
        await _mockMessageService.Received(1).SendAndSaveAnimationAsync(
            123,
            Arg.Any<InputFile>(),
            Arg.Is<TelegramMessage>(m => m!.Text == "Ban #0!"),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region File Caching Tests

    [Test]
    public async Task SendBanCelebration_WhenGifSentSuccessfully_UpdatesFileIdCache()
    {
        // Arrange - GIF has no cached FileId (empty string triggers local upload path)
        // Create a real temp file since the service calls System.IO.File.Exists()
        var tempDir = Path.Combine(Path.GetTempPath(), "ban-celebration-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "1.gif");
        await File.WriteAllBytesAsync(tempFile, [0x47, 0x49, 0x46]); // GIF magic bytes

        try
        {
            var gif = new BanCelebrationGif { Id = 1, FilePath = "ban-gifs/1.gif", FileId = "" };
            var caption = new BanCelebrationCaption { Id = 1, Text = "Banned!", DmText = "Banned" };

            _mockGifRepository.ClaimNextForCycleAsync(Arg.Any<CancellationToken>()).Returns(gif);
            _mockGifRepository.GetFullPath(gif.FilePath).Returns(tempFile);
            _mockCaptionRepository.ClaimNextForCycleAsync(Arg.Any<CancellationToken>()).Returns(caption);

            // Setup SendAndSaveAnimationAsync to return message with new FileId from Telegram
            var sentMessage = new Message
            {
                Animation = new Animation { FileId = "new_telegram_file_id" }
            };
            _mockMessageService.SendAndSaveAnimationAsync(
                Arg.Any<long>(),
                Arg.Any<InputFile>(),
                Arg.Any<TelegramMessage>(),
                Arg.Any<CancellationToken>())
                .Returns(sentMessage);

            // Act
            var result = await _sut.SendBanCelebrationAsync(new ChatIdentity(123, "Chat"), new UserIdentity(456, "User", null, null), true);

            // Assert
            Assert.That(result, Is.True);

            // Verify the file_id was cached back to the repository
            await _mockGifRepository.Received(1).UpdateFileIdAsync(
                gif.Id,
                "new_telegram_file_id",
                Arg.Any<CancellationToken>());
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task SendBanCelebration_WhenGifAlreadyCached_DoesNotUpdateFileId()
    {
        // Arrange - GIF already has a cached FileId
        var gif = new BanCelebrationGif { Id = 1, FilePath = "ban-gifs/1.gif", FileId = "existing_file_id" };
        var caption = new BanCelebrationCaption { Id = 1, Text = "Banned!", DmText = "Banned" };

        _mockGifRepository.ClaimNextForCycleAsync(Arg.Any<CancellationToken>()).Returns(gif);
        _mockCaptionRepository.ClaimNextForCycleAsync(Arg.Any<CancellationToken>()).Returns(caption);

        // Setup SendAndSaveAnimationAsync — returns message with same FileId (cached send)
        SetupSuccessfulSendAnimation("existing_file_id");

        // Act
        var result = await _sut.SendBanCelebrationAsync(new ChatIdentity(123, "Chat"), new UserIdentity(456, "User", null, null), true);

        // Assert
        Assert.That(result, Is.True);

        // Verify UpdateFileIdAsync was NOT called (FileId was already cached)
        await _mockGifRepository.DidNotReceive().UpdateFileIdAsync(
            Arg.Any<int>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendBanCelebration_WhenCachedFileIdIsStale_ClearsAndFallsBackToLocalUpload()
    {
        // Arrange - GIF has a stale cached FileId
        var tempDir = Path.Combine(Path.GetTempPath(), "ban-celebration-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "1.gif");
        await File.WriteAllBytesAsync(tempFile, [0x47, 0x49, 0x46]); // GIF magic bytes

        try
        {
            var gif = new BanCelebrationGif { Id = 1, FilePath = "ban-gifs/1.gif", FileId = "stale_file_id" };
            var caption = new BanCelebrationCaption { Id = 1, Text = "Banned!", DmText = "Banned" };

            _mockGifRepository.ClaimNextForCycleAsync(Arg.Any<CancellationToken>()).Returns(gif);
            _mockGifRepository.GetFullPath(gif.FilePath).Returns(tempFile);
            _mockCaptionRepository.ClaimNextForCycleAsync(Arg.Any<CancellationToken>()).Returns(caption);

            // First call with cached file_id throws "wrong file identifier" (stale)
            // Second call with local file stream succeeds
            var successMessage = new Message
            {
                Animation = new Animation { FileId = "new_file_id_from_upload" }
            };
            _mockMessageService.SendAndSaveAnimationAsync(
                Arg.Any<long>(),
                Arg.Any<InputFile>(),
                Arg.Any<TelegramMessage>(),
                Arg.Any<CancellationToken>())
                .Returns(
                    _ => throw new Exception("Bad Request: wrong file identifier/HTTP URL specified"),
                    _ => successMessage);

            // Act
            var result = await _sut.SendBanCelebrationAsync(new ChatIdentity(123, "Chat"), new UserIdentity(456, "User", null, null), true);

            // Assert
            Assert.That(result, Is.True);

            // Verify stale file_id was cleared
            await _mockGifRepository.Received(1).ClearFileIdAsync(1, Arg.Any<CancellationToken>());

            // Verify SendAndSaveAnimationAsync was called twice (stale attempt + local upload)
            await _mockMessageService.Received(2).SendAndSaveAnimationAsync(
                Arg.Any<long>(),
                Arg.Any<InputFile>(),
                Arg.Any<TelegramMessage>(),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    #endregion

    #region Exception Handling Tests

    [Test]
    public async Task SendBanCelebration_WhenExceptionOccurs_ReturnsFalseAndLogsWarning()
    {
        // Arrange
        _mockConfigService.GetEffectiveBanCelebrationAsync(Arg.Any<long>())
            .ThrowsAsync(new InvalidOperationException("Database error"));

        // Act
        var result = await _sut.SendBanCelebrationAsync(new ChatIdentity(123, "Chat"), new UserIdentity(456, "User", null, null), true);

        // Assert
        Assert.That(result, Is.False, "Should return false when exception occurs");

        // Verify warning was logged
        _mockLogger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o!.ToString()!.Contains("Failed to send ban celebration")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Test]
    public async Task SendBanCelebration_WhenMessageServiceThrows_ReturnsFalse()
    {
        // Arrange
        var gif = new BanCelebrationGif { Id = 1, FilePath = "ban-gifs/1.gif", FileId = "file1" };
        var caption = new BanCelebrationCaption { Id = 1, Text = "Banned!", DmText = "Banned" };

        _mockGifRepository.ClaimNextForCycleAsync(Arg.Any<CancellationToken>()).Returns(gif);
        _mockCaptionRepository.ClaimNextForCycleAsync(Arg.Any<CancellationToken>()).Returns(caption);

        // Message service throws (Telegram API error)
        _mockMessageService.SendAndSaveAnimationAsync(
            Arg.Any<long>(),
            Arg.Any<InputFile>(),
            Arg.Any<TelegramMessage>(),
            Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Telegram API rate limited"));

        // Act
        var result = await _sut.SendBanCelebrationAsync(new ChatIdentity(123, "Chat"), new UserIdentity(456, "User", null, null), true);

        // Assert - Service handles exception gracefully and returns false
        Assert.That(result, Is.False);
    }

    #endregion

    #region Explicit Username Masking Tests

    [Test]
    public async Task SendBanCelebrationAsync_AiFlaggedAndMaskingOn_CaptionContainsRedactionText()
    {
        var scan = new ProfileScanResultRecord(
            Id: 1,
            UserId: TestUserId,
            ScannedAt: DateTimeOffset.UtcNow,
            Score: 4.5m,
            Outcome: ProfileScanOutcome.Banned,
            RuleScore: 0.0m,
            AiScore: 4.5m,
            AiReason: "explicit handle",
            AiSignals: "explicit_handle",
            ExplicitDisplayText: true);

        _mockScanRepository.GetLatestByUserIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(scan);

        EnableProfileScanConfig(maskExplicitUsername: true, redactionText: "[explicit username redacted]");
        SeedOneGifAndOneCaption("{username} got banned!");

        await _sut.SendBanCelebrationAsync(
            chat: TestChat,
            bannedUser: TestBannedUser,
            isAutoBan: true,
            cancellationToken: CancellationToken.None);

        await _mockMessageService.Received(1).SendAndSaveAnimationAsync(
            TestChatId,
            Arg.Any<InputFile>(),
            Arg.Is<TelegramMessage>(m => m!.Text.Contains("[explicit username redacted]")
                                      && !m.Text.Contains(TestBannedUser.DisplayName)),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendBanCelebrationAsync_AiFlaggedButMaskingOff_CaptionContainsDisplayName()
    {
        var scan = new ProfileScanResultRecord(
            Id: 1,
            UserId: TestUserId,
            ScannedAt: DateTimeOffset.UtcNow,
            Score: 4.5m,
            Outcome: ProfileScanOutcome.Banned,
            RuleScore: 0.0m,
            AiScore: 4.5m,
            AiReason: "explicit handle",
            AiSignals: "explicit_handle",
            ExplicitDisplayText: true);

        _mockScanRepository.GetLatestByUserIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(scan);

        EnableProfileScanConfig(maskExplicitUsername: false, redactionText: "[explicit username redacted]");
        SeedOneGifAndOneCaption("{username} got banned!");

        await _sut.SendBanCelebrationAsync(
            chat: TestChat,
            bannedUser: TestBannedUser,
            isAutoBan: true,
            cancellationToken: CancellationToken.None);

        await _mockMessageService.Received(1).SendAndSaveAnimationAsync(
            TestChatId,
            Arg.Any<InputFile>(),
            Arg.Is<TelegramMessage>(m => m!.Text.Contains(TestBannedUser.DisplayName)
                                      && !m.Text.Contains("[explicit username redacted]")),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendBanCelebrationAsync_NoScanRecord_CaptionContainsDisplayName()
    {
        _mockScanRepository.GetLatestByUserIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns((ProfileScanResultRecord?)null);

        EnableProfileScanConfig(maskExplicitUsername: true, redactionText: "[explicit username redacted]");
        SeedOneGifAndOneCaption("{username} got banned!");

        await _sut.SendBanCelebrationAsync(
            chat: TestChat,
            bannedUser: TestBannedUser,
            isAutoBan: true,
            cancellationToken: CancellationToken.None);

        await _mockMessageService.Received(1).SendAndSaveAnimationAsync(
            TestChatId,
            Arg.Any<InputFile>(),
            Arg.Is<TelegramMessage>(m => m!.Text.Contains(TestBannedUser.DisplayName)
                                      && !m.Text.Contains("[explicit username redacted]")),
            Arg.Any<CancellationToken>());
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("\t\n")]
    public async Task SendBanCelebrationAsync_AiFlaggedAndRedactionTextBlank_FallsBackToDefault(string blankRedactionText)
    {
        var scan = new ProfileScanResultRecord(
            Id: 1,
            UserId: TestUserId,
            ScannedAt: DateTimeOffset.UtcNow,
            Score: 4.5m,
            Outcome: ProfileScanOutcome.Banned,
            RuleScore: 0.0m,
            AiScore: 4.5m,
            AiReason: "explicit handle",
            AiSignals: "explicit_handle",
            ExplicitDisplayText: true);

        _mockScanRepository.GetLatestByUserIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(scan);

        EnableProfileScanConfig(maskExplicitUsername: true, redactionText: blankRedactionText);
        SeedOneGifAndOneCaption("{username} got banned!");

        await _sut.SendBanCelebrationAsync(
            chat: TestChat,
            bannedUser: TestBannedUser,
            isAutoBan: true,
            cancellationToken: CancellationToken.None);

        await _mockMessageService.Received(1).SendAndSaveAnimationAsync(
            TestChatId,
            Arg.Any<InputFile>(),
            Arg.Is<TelegramMessage>(m => m!.Text.Contains(ProfileScanConfig.DefaultExplicitUsernameRedactionText)
                                      && !m.Text.Contains(TestBannedUser.DisplayName)),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Entity Overload Tests

    [Test]
    public async Task SendBanCelebration_ChatGif_UsesEntityAnimationOverload_NotStringParseModeOverload()
    {
        // Arrange
        _mockGifRepository.ClaimNextForCycleAsync(Arg.Any<CancellationToken>())
            .Returns(new BanCelebrationGif { Id = 1, FilePath = "ban-gifs/1.gif", FileId = "file1" });
        _mockCaptionRepository.ClaimNextForCycleAsync(Arg.Any<CancellationToken>())
            .Returns(new BanCelebrationCaption { Id = 1, Text = "Caption 1", DmText = "DM 1" });

        _mockMessageService.SendAndSaveAnimationAsync(
                Arg.Any<long>(),
                Arg.Any<InputFile>(),
                Arg.Any<TelegramMessage>(),
                Arg.Any<CancellationToken>())
            .Returns(new Message { Animation = new Animation { FileId = "file1" } });

        // Act
        var result = await _sut.SendBanCelebrationAsync(TestChat, TestBannedUser, isAutoBan: true);

        // Assert — entity overload called, old string+ParseMode overload NOT called
        Assert.That(result, Is.True);
        await _mockMessageService.Received(1).SendAndSaveAnimationAsync(
            Arg.Any<long>(),
            Arg.Any<InputFile>(),
            Arg.Any<TelegramMessage>(),
            Arg.Any<CancellationToken>());
        await _mockMessageService.DidNotReceive().SendAndSaveAnimationAsync(
            Arg.Any<long>(),
            Arg.Any<InputFile>(),
            Arg.Any<string>(),
            Arg.Any<ParseMode?>(),
            Arg.Any<IReadOnlyList<MessageEntity>?>(),
            Arg.Any<CancellationToken>());
    }

    #endregion
}
