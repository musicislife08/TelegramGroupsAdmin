using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Core;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Results;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Handlers;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.Moderation;

/// <summary>
/// Unit tests for ModerationOrchestrator.
/// Tests business rules (bans revoke trust, N warnings = auto-ban) and workflow composition.
/// The orchestrator is the "boss" that coordinates handlers and owns business rules.
/// </summary>
[TestFixture]
public class ModerationOrchestratorTests
{
    private const long TestChatId = 123456789L;
    private IBanHandler _mockBanHandler = null!;
    private ITrustHandler _mockTrustHandler = null!;
    private IWarnHandler _mockWarnHandler = null!;
    private IMessageHandler _mockMessageHandler = null!;
    private IRestrictHandler _mockRestrictHandler = null!;
    private IAuditHandler _mockAuditHandler = null!;
    private INotificationHandler _mockNotificationHandler = null!;
    private ITrainingHandler _mockTrainingHandler = null!;
    private ITelegramUserRepository _mockUserRepository = null!;
    private IConfigService _mockConfigService = null!;
    private ILogger<ModerationOrchestrator> _mockLogger = null!;
    private ModerationOrchestrator _orchestrator = null!;

    [SetUp]
    public void SetUp()
    {
        _mockBanHandler = Substitute.For<IBanHandler>();
        _mockTrustHandler = Substitute.For<ITrustHandler>();
        _mockWarnHandler = Substitute.For<IWarnHandler>();
        _mockMessageHandler = Substitute.For<IMessageHandler>();
        _mockRestrictHandler = Substitute.For<IRestrictHandler>();
        _mockAuditHandler = Substitute.For<IAuditHandler>();
        _mockNotificationHandler = Substitute.For<INotificationHandler>();
        _mockTrainingHandler = Substitute.For<ITrainingHandler>();
        _mockUserRepository = Substitute.For<ITelegramUserRepository>();
        _mockConfigService = Substitute.For<IConfigService>();
        _mockLogger = Substitute.For<ILogger<ModerationOrchestrator>>();

        _orchestrator = new ModerationOrchestrator(
            _mockBanHandler,
            _mockTrustHandler,
            _mockWarnHandler,
            _mockMessageHandler,
            _mockRestrictHandler,
            _mockAuditHandler,
            _mockNotificationHandler,
            _mockTrainingHandler,
            _mockUserRepository,
            _mockConfigService,
            _mockLogger);
    }

    #region System Account Protection Tests

    [Test]
    [TestCase(777000, Description = "Service account")]
    [TestCase(1087968824, Description = "Anonymous admin bot")]
    [TestCase(136817688, Description = "Channel bot")]
    [TestCase(1271266957, Description = "Replies bot")]
    [TestCase(5434988373, Description = "Antispam bot")]
    public async Task BanUserAsync_TelegramSystemAccount_ReturnsError(long systemUserId)
    {
        // Arrange - All Telegram system accounts should be protected from moderation

        // Act
        var result = await _orchestrator.BanUserAsync(
            systemUserId,
            null,
            Actor.FromSystem("test"),
            "Attempted ban of system account");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("system account"));
        });

        // Verify no handler was called
        await _mockBanHandler.DidNotReceive().BanAsync(
            Arg.Any<long>(),
            Arg.Any<Actor>(),
            Arg.Any<string>(),
            Arg.Any<long?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    [TestCase(777000, Description = "Service account")]
    [TestCase(1087968824, Description = "Anonymous admin bot")]
    [TestCase(136817688, Description = "Channel bot")]
    [TestCase(1271266957, Description = "Replies bot")]
    [TestCase(5434988373, Description = "Antispam bot")]
    public async Task WarnUserAsync_TelegramSystemAccount_ReturnsError(long systemUserId)
    {
        // Arrange - All Telegram system accounts should be protected from warnings

        // Act
        var result = await _orchestrator.WarnUserAsync(
            systemUserId,
            null,
            Actor.FromSystem("test"),
            "Attempted warning of system account",
            TestChatId);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("system account"));
    }

    [Test]
    [TestCase(777000, Description = "Service account")]
    [TestCase(1087968824, Description = "Anonymous admin bot")]
    [TestCase(136817688, Description = "Channel bot")]
    [TestCase(1271266957, Description = "Replies bot")]
    [TestCase(5434988373, Description = "Antispam bot")]
    public async Task TempBanUserAsync_TelegramSystemAccount_ReturnsError(long systemUserId)
    {
        // Arrange - All Telegram system accounts should be protected from temp bans

        // Act
        var result = await _orchestrator.TempBanUserAsync(
            systemUserId,
            null,
            Actor.FromSystem("test"),
            "Test",
            TimeSpan.FromHours(1));

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("system account"));
    }

    #endregion

    #region BanUserAsync Tests

    [Test]
    public async Task BanUserAsync_SuccessfulBan_RevokesTrustAndNotifiesAdmins()
    {
        // Arrange
        const long userId = 12345L;
        var executor = Actor.FromSystem("SpamDetection");

        _mockBanHandler.BanAsync(userId, executor, Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(BanResult.Succeeded(chatsAffected: 5, chatsFailed: 0));

        _mockTrustHandler.UntrustAsync(userId, executor, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(UntrustResult.Succeeded());

        // Act
        var result = await _orchestrator.BanUserAsync(userId, null, executor, "Spam violation");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.ChatsAffected, Is.EqualTo(5));
            Assert.That(result.TrustRemoved, Is.True);
        });

        // Verify business rule: bans always revoke trust
        await _mockTrustHandler.Received(1).UntrustAsync(
            userId,
            executor,
            Arg.Is<string>(s => s!.Contains("ban")),
            Arg.Any<CancellationToken>());

        // Verify audit was logged
        await _mockAuditHandler.Received(1).LogBanAsync(
            userId, executor, Arg.Any<string>(), Arg.Any<CancellationToken>());

        // Verify admins were notified
        await _mockNotificationHandler.Received(1).NotifyAdminsBanAsync(
            userId, executor, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task BanUserAsync_BanFails_DoesNotRevokeTrust()
    {
        // Arrange
        const long userId = 12345L;
        var executor = Actor.FromSystem("test");

        _mockBanHandler.BanAsync(userId, executor, Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(BanResult.Failed("API error"));

        // Act
        var result = await _orchestrator.BanUserAsync(userId, null, executor, "Test");

        // Assert
        Assert.That(result.Success, Is.False);

        // Verify trust was NOT revoked (ban failed)
        await _mockTrustHandler.DidNotReceive().UntrustAsync(
            Arg.Any<long>(),
            Arg.Any<Actor>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region WarnUserAsync Tests - Auto-Ban Threshold

    [Test]
    public async Task WarnUserAsync_BelowThreshold_DoesNotTriggerAutoBan()
    {
        // Arrange
        const long userId = 12345L;
        var executor = Actor.FromSystem("SpamDetection");

        _mockWarnHandler.WarnAsync(userId, executor, Arg.Any<string>(), Arg.Any<long>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(WarnResult.Succeeded(warningCount: 2)); // Below threshold

        _mockConfigService.GetEffectiveAsync<WarningSystemConfig>(
                ConfigType.Moderation, Arg.Any<long>())
            .Returns(new WarningSystemConfig { AutoBanEnabled = true, AutoBanThreshold = 3 });

        // Act
        var result = await _orchestrator.WarnUserAsync(userId, null, executor, "First warning", TestChatId);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.WarningCount, Is.EqualTo(2));
            Assert.That(result.AutoBanTriggered, Is.False);
        });

        // Verify ban was NOT called
        await _mockBanHandler.DidNotReceive().BanAsync(
            Arg.Any<long>(),
            Arg.Any<Actor>(),
            Arg.Any<string>(),
            Arg.Any<long?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task WarnUserAsync_ReachesThreshold_TriggersAutoBan()
    {
        // Arrange
        const long userId = 12345L;
        var executor = Actor.FromSystem("SpamDetection");

        _mockWarnHandler.WarnAsync(userId, executor, Arg.Any<string>(), Arg.Any<long>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(WarnResult.Succeeded(warningCount: 3)); // Reaches threshold

        _mockConfigService.GetEffectiveAsync<WarningSystemConfig>(
                ConfigType.Moderation, Arg.Any<long>())
            .Returns(new WarningSystemConfig { AutoBanEnabled = true, AutoBanThreshold = 3 });

        _mockBanHandler.BanAsync(Arg.Any<long>(), Arg.Any<Actor>(), Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(BanResult.Succeeded(chatsAffected: 5, chatsFailed: 0));

        _mockTrustHandler.UntrustAsync(Arg.Any<long>(), Arg.Any<Actor>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(UntrustResult.Succeeded());

        // Act
        var result = await _orchestrator.WarnUserAsync(userId, null, executor, "Final warning", TestChatId);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.WarningCount, Is.EqualTo(3));
            Assert.That(result.AutoBanTriggered, Is.True);
            Assert.That(result.ChatsAffected, Is.EqualTo(5));
        });

        // Verify auto-ban was triggered with Actor.AutoBan
        await _mockBanHandler.Received(1).BanAsync(
            userId,
            Actor.AutoBan,
            Arg.Is<string>(s => s!.Contains("threshold")),
            Arg.Any<long?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task WarnUserAsync_AutoBanDisabled_DoesNotTriggerAutoBan()
    {
        // Arrange
        const long userId = 12345L;
        var executor = Actor.FromSystem("SpamDetection");

        _mockWarnHandler.WarnAsync(userId, executor, Arg.Any<string>(), Arg.Any<long>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(WarnResult.Succeeded(warningCount: 5)); // Well above threshold

        _mockConfigService.GetEffectiveAsync<WarningSystemConfig>(
                ConfigType.Moderation, Arg.Any<long>())
            .Returns(new WarningSystemConfig { AutoBanEnabled = false, AutoBanThreshold = 3 });

        // Act
        var result = await _orchestrator.WarnUserAsync(userId, null, executor, "Warning", TestChatId);

        // Assert
        Assert.That(result.AutoBanTriggered, Is.False);

        // Verify ban was NOT called even though count exceeds threshold
        await _mockBanHandler.DidNotReceive().BanAsync(
            Arg.Any<long>(),
            Arg.Any<Actor>(),
            Arg.Any<string>(),
            Arg.Any<long?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task WarnUserAsync_UserNotified_AfterSuccessfulWarning()
    {
        // Arrange
        const long userId = 12345L;
        var executor = Actor.FromSystem("SpamDetection");

        _mockWarnHandler.WarnAsync(userId, executor, Arg.Any<string>(), Arg.Any<long>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(WarnResult.Succeeded(warningCount: 1));

        _mockConfigService.GetEffectiveAsync<WarningSystemConfig>(
                ConfigType.Moderation, Arg.Any<long>())
            .Returns(WarningSystemConfig.Default);

        // Act
        var result = await _orchestrator.WarnUserAsync(userId, null, executor, "Spam detected", TestChatId);

        // Assert
        Assert.That(result.Success, Is.True);

        // Verify user was notified about their warning
        await _mockNotificationHandler.Received(1).NotifyUserWarningAsync(
            userId,
            1, // warning count
            "Spam detected",
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region MarkAsSpamAndBanAsync Tests

    [Test]
    public async Task MarkAsSpamAndBanAsync_CompletesAllSteps_InCorrectOrder()
    {
        // Arrange
        const long messageId = 42L;
        const long userId = 12345L;
        const long chatId = -100123456789L;
        var executor = Actor.FromSystem("SpamDetection");

        _mockMessageHandler.EnsureExistsAsync(messageId, chatId, Arg.Any<global::Telegram.Bot.Types.Message?>(), Arg.Any<CancellationToken>())
            .Returns(BackfillResult.AlreadyExists());

        _mockMessageHandler.DeleteAsync(chatId, messageId, executor, Arg.Any<CancellationToken>())
            .Returns(DeleteResult.Succeeded(messageDeleted: true));

        _mockBanHandler.BanAsync(userId, executor, Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(BanResult.Succeeded(chatsAffected: 5, chatsFailed: 0));

        _mockTrustHandler.UntrustAsync(userId, executor, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(UntrustResult.Succeeded());

        // Act
        var result = await _orchestrator.MarkAsSpamAndBanAsync(
            messageId, userId, chatId, executor, "Spam detected");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.MessageDeleted, Is.True);
            Assert.That(result.ChatsAffected, Is.EqualTo(5));
            Assert.That(result.TrustRemoved, Is.True);
        });

        // Verify all steps were called
        await _mockMessageHandler.Received(1).EnsureExistsAsync(
            messageId, chatId, Arg.Any<global::Telegram.Bot.Types.Message?>(), Arg.Any<CancellationToken>());

        await _mockMessageHandler.Received(1).DeleteAsync(
            chatId, messageId, executor, Arg.Any<CancellationToken>());

        await _mockBanHandler.Received(1).BanAsync(
            userId, executor, Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>());

        await _mockTrainingHandler.Received(1).CreateSpamSampleAsync(
            messageId, executor, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task MarkAsSpamAndBanAsync_DeleteFails_StillBansUser()
    {
        // Arrange - Message deletion is best-effort (may already be deleted)
        const long messageId = 42L;
        const long userId = 12345L;
        const long chatId = -100123456789L;
        var executor = Actor.FromSystem("SpamDetection");

        _mockMessageHandler.EnsureExistsAsync(messageId, chatId, Arg.Any<global::Telegram.Bot.Types.Message?>(), Arg.Any<CancellationToken>())
            .Returns(BackfillResult.AlreadyExists());

        _mockMessageHandler.DeleteAsync(chatId, messageId, executor, Arg.Any<CancellationToken>())
            .Returns(DeleteResult.Succeeded(messageDeleted: false)); // Deletion failed (soft failure)

        _mockBanHandler.BanAsync(userId, executor, Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(BanResult.Succeeded(chatsAffected: 5, chatsFailed: 0));

        _mockTrustHandler.UntrustAsync(userId, executor, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(UntrustResult.Succeeded());

        // Act
        var result = await _orchestrator.MarkAsSpamAndBanAsync(
            messageId, userId, chatId, executor, "Spam detected");

        // Assert - Overall success even though message wasn't deleted
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.MessageDeleted, Is.False);
            Assert.That(result.ChatsAffected, Is.EqualTo(5));
        });
    }

    [Test]
    public async Task MarkAsSpamAndBanAsync_BanFails_ReturnsFailure()
    {
        // Arrange
        const long messageId = 42L;
        const long userId = 12345L;
        const long chatId = -100123456789L;
        var executor = Actor.FromSystem("SpamDetection");

        _mockMessageHandler.EnsureExistsAsync(messageId, chatId, Arg.Any<global::Telegram.Bot.Types.Message?>(), Arg.Any<CancellationToken>())
            .Returns(BackfillResult.AlreadyExists());

        _mockMessageHandler.DeleteAsync(chatId, messageId, executor, Arg.Any<CancellationToken>())
            .Returns(DeleteResult.Succeeded(messageDeleted: true));

        _mockBanHandler.BanAsync(userId, executor, Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(BanResult.Failed("API error"));

        // Act
        var result = await _orchestrator.MarkAsSpamAndBanAsync(
            messageId, userId, chatId, executor, "Spam detected");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.MessageDeleted, Is.True); // Message was deleted before ban failed
        });

        // Training data should NOT be created on failure
        await _mockTrainingHandler.DidNotReceive().CreateSpamSampleAsync(
            Arg.Any<long>(),
            Arg.Any<Actor>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region TrustUserAsync Tests

    [Test]
    public async Task TrustUserAsync_SuccessfulTrust_AuditsAction()
    {
        // Arrange
        const long userId = 12345L;
        var executor = Actor.FromTelegramUser(999, "Admin");

        _mockTrustHandler.TrustAsync(userId, executor, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TrustResult.Succeeded());

        // Act
        var result = await _orchestrator.TrustUserAsync(userId, executor, "Verified user");

        // Assert
        Assert.That(result.Success, Is.True);

        // Verify audit was logged
        await _mockAuditHandler.Received(1).LogTrustAsync(
            userId, executor, "Verified user", Arg.Any<CancellationToken>());
    }

    #endregion

    #region UnbanUserAsync Tests

    [Test]
    public async Task UnbanUserAsync_WithRestoreTrust_RestoresTrust()
    {
        // Arrange
        const long userId = 12345L;
        var executor = Actor.FromWebUser("web-user", "admin@test.com");

        _mockBanHandler.UnbanAsync(userId, executor, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(UnbanResult.Succeeded(chatsAffected: 5, chatsFailed: 0));

        _mockTrustHandler.TrustAsync(userId, executor, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TrustResult.Succeeded());

        // Act
        var result = await _orchestrator.UnbanUserAsync(
            userId, executor, "False positive", restoreTrust: true);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.ChatsAffected, Is.EqualTo(5));
            Assert.That(result.TrustRestored, Is.True);
        });

        // Verify trust was restored
        await _mockTrustHandler.Received(1).TrustAsync(
            userId, executor, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UnbanUserAsync_WithoutRestoreTrust_DoesNotRestoreTrust()
    {
        // Arrange
        const long userId = 12345L;
        var executor = Actor.FromWebUser("web-user", "admin@test.com");

        _mockBanHandler.UnbanAsync(userId, executor, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(UnbanResult.Succeeded(chatsAffected: 5, chatsFailed: 0));

        // Act
        var result = await _orchestrator.UnbanUserAsync(
            userId, executor, "Ban expired", restoreTrust: false);

        // Assert
        Assert.That(result.TrustRestored, Is.False);

        // Verify trust was NOT restored
        await _mockTrustHandler.DidNotReceive().TrustAsync(
            Arg.Any<long>(),
            Arg.Any<Actor>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region TempBanUserAsync Tests

    [Test]
    public async Task TempBanUserAsync_SuccessfulTempBan_NotifiesUser()
    {
        // Arrange
        const long userId = 12345L;
        var executor = Actor.FromSystem("SpamDetection");
        var duration = TimeSpan.FromHours(24);
        var expiresAt = DateTimeOffset.UtcNow.Add(duration);

        _mockBanHandler.TempBanAsync(userId, executor, duration, Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(TempBanResult.Succeeded(chatsAffected: 5, expiresAt, chatsFailed: 0));

        // Act
        var result = await _orchestrator.TempBanUserAsync(
            userId, null, executor, "Temporary mute for spam", duration);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.ChatsAffected, Is.EqualTo(5));
        });

        // Verify user was notified about temp ban
        await _mockNotificationHandler.Received(1).NotifyUserTempBanAsync(
            userId,
            duration,
            Arg.Any<DateTimeOffset>(),
            "Temporary mute for spam",
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TempBanUserAsync_TempBanFails_DoesNotNotify()
    {
        // Arrange
        const long userId = 12345L;
        var executor = Actor.FromSystem("test");
        var duration = TimeSpan.FromHours(1);

        _mockBanHandler.TempBanAsync(userId, executor, duration, Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(TempBanResult.Failed("API error"));

        // Act
        var result = await _orchestrator.TempBanUserAsync(
            userId, null, executor, "Test", duration);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("API error"));
        });

        // Verify notification was NOT sent on failure
        await _mockNotificationHandler.DidNotReceive().NotifyUserTempBanAsync(
            Arg.Any<long>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TempBanUserAsync_PartialSuccess_StillNotifiesUser()
    {
        // Arrange - Some chats succeed, some fail
        const long userId = 12345L;
        var executor = Actor.FromTelegramUser(999, "Admin");
        var duration = TimeSpan.FromHours(2);
        var expiresAt = DateTimeOffset.UtcNow.Add(duration);

        _mockBanHandler.TempBanAsync(userId, executor, duration, Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(TempBanResult.Succeeded(chatsAffected: 3, expiresAt, chatsFailed: 2));

        // Act
        var result = await _orchestrator.TempBanUserAsync(
            userId, null, executor, "Spam warning", duration);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.ChatsAffected, Is.EqualTo(3));
        });

        // Verify user was notified even with partial success
        await _mockNotificationHandler.Received(1).NotifyUserTempBanAsync(
            userId,
            duration,
            Arg.Any<DateTimeOffset>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region RestrictUserAsync Tests

    [Test]
    [TestCase(777000, Description = "Service account")]
    [TestCase(1087968824, Description = "Anonymous admin bot")]
    [TestCase(136817688, Description = "Channel bot")]
    [TestCase(1271266957, Description = "Replies bot")]
    [TestCase(5434988373, Description = "Antispam bot")]
    public async Task RestrictUserAsync_TelegramSystemAccount_ReturnsError(long systemUserId)
    {
        // Arrange - All Telegram system accounts should be protected from restrictions

        // Act
        var result = await _orchestrator.RestrictUserAsync(
            systemUserId,
            null,
            Actor.FromSystem("test"),
            "Test",
            TimeSpan.FromHours(1));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("system account"));
        });

        // Verify no handler was called
        await _mockRestrictHandler.DidNotReceive().RestrictAsync(
            Arg.Any<long>(),
            Arg.Any<long>(),
            Arg.Any<Actor>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RestrictUserAsync_SuccessfulRestrict_AuditsAction()
    {
        // Arrange
        const long userId = 12345L;
        const long chatId = 67890L;
        var executor = Actor.FromSystem("WelcomeFlow");
        var duration = TimeSpan.FromMinutes(15);
        var expiresAt = DateTimeOffset.UtcNow.Add(duration);

        _mockRestrictHandler.RestrictAsync(userId, chatId, executor, duration, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(RestrictResult.Succeeded(chatsAffected: 1, expiresAt, chatsFailed: 0));

        // Act
        var result = await _orchestrator.RestrictUserAsync(
            userId, null, executor, "Welcome mute", duration, chatId);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.ChatsAffected, Is.EqualTo(1));
        });

        // Verify audit was logged
        await _mockAuditHandler.Received(1).LogRestrictAsync(
            userId, chatId, executor, "Welcome mute", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RestrictUserAsync_GlobalRestrict_UsesZeroChatId()
    {
        // Arrange - chatId=null means global restriction
        const long userId = 12345L;
        var executor = Actor.FromSystem("SpamDetection");
        var duration = TimeSpan.FromHours(1);
        var expiresAt = DateTimeOffset.UtcNow.Add(duration);

        _mockRestrictHandler.RestrictAsync(userId, 0, executor, duration, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(RestrictResult.Succeeded(chatsAffected: 5, expiresAt, chatsFailed: 0));

        // Act
        var result = await _orchestrator.RestrictUserAsync(
            userId, null, executor, "Global mute", duration, chatId: null);

        // Assert
        Assert.That(result.Success, Is.True);

        // Verify RestrictHandler was called with chatId=0 (global sentinel)
        await _mockRestrictHandler.Received(1).RestrictAsync(
            userId,
            0, // Global sentinel
            executor,
            duration,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RestrictUserAsync_RestrictionFails_ReturnsFailure()
    {
        // Arrange
        const long userId = 12345L;
        const long chatId = 67890L;
        var executor = Actor.FromSystem("test");

        _mockRestrictHandler.RestrictAsync(userId, chatId, executor, Arg.Any<TimeSpan>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(RestrictResult.Failed("User is admin"));

        // Act
        var result = await _orchestrator.RestrictUserAsync(
            userId, null, executor, "Test", TimeSpan.FromHours(1), chatId);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("User is admin"));
        });

        // Verify audit was NOT logged on failure
        await _mockAuditHandler.DidNotReceive().LogRestrictAsync(
            Arg.Any<long>(),
            Arg.Any<long>(),
            Arg.Any<Actor>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region DeleteMessageAsync Tests

    [Test]
    public async Task DeleteMessageAsync_SuccessfulDelete_AuditsAction()
    {
        // Arrange
        const long messageId = 42L;
        const long chatId = -100123456789L;
        const long userId = 12345L;
        var executor = Actor.FromTelegramUser(999, "Admin");

        _mockMessageHandler.DeleteAsync(chatId, messageId, executor, Arg.Any<CancellationToken>())
            .Returns(DeleteResult.Succeeded(messageDeleted: true));

        // Act
        var result = await _orchestrator.DeleteMessageAsync(
            messageId, chatId, userId, executor, "Spam");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.MessageDeleted, Is.True);
        });

        // Verify audit was logged
        await _mockAuditHandler.Received(1).LogDeleteAsync(
            messageId, chatId, userId, executor, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeleteMessageAsync_MessageAlreadyDeleted_StillAudits()
    {
        // Arrange - Message was already deleted (returns success but messageDeleted=false)
        const long messageId = 42L;
        const long chatId = -100123456789L;
        const long userId = 12345L;
        var executor = Actor.FromTelegramUser(999, "Admin");

        _mockMessageHandler.DeleteAsync(chatId, messageId, executor, Arg.Any<CancellationToken>())
            .Returns(DeleteResult.Succeeded(messageDeleted: false));

        // Act
        var result = await _orchestrator.DeleteMessageAsync(
            messageId, chatId, userId, executor, "Spam");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.MessageDeleted, Is.False);
        });

        // Verify audit was still logged (deletion attempt recorded)
        await _mockAuditHandler.Received(1).LogDeleteAsync(
            messageId, chatId, userId, executor, Arg.Any<CancellationToken>());
    }

    #endregion

    #region Business Rule Failure Recovery Tests

    [Test]
    public async Task BanUserAsync_TrustRevocationFails_StillReturnsSuccess()
    {
        // Arrange - Ban succeeds but trust revocation fails
        const long userId = 12345L;
        var executor = Actor.FromSystem("SpamDetection");

        _mockBanHandler.BanAsync(userId, executor, Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(BanResult.Succeeded(chatsAffected: 5, chatsFailed: 0));

        _mockTrustHandler.UntrustAsync(userId, executor, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(UntrustResult.Failed("Database error"));

        // Act
        var result = await _orchestrator.BanUserAsync(userId, null, executor, "Spam violation");

        // Assert - Overall success (ban completed), but TrustRemoved=false
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.ChatsAffected, Is.EqualTo(5));
            Assert.That(result.TrustRemoved, Is.False, "Trust revocation failed, so should be false");
        });

        // Verify: Ban audit logged, but Untrust audit NOT logged (it failed)
        await _mockAuditHandler.Received(1).LogBanAsync(
            userId, executor, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _mockAuditHandler.DidNotReceive().LogUntrustAsync(
            Arg.Any<long>(), Arg.Any<Actor>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        // Verify: Admins still notified (non-critical failure doesn't block notification)
        await _mockNotificationHandler.Received(1).NotifyAdminsBanAsync(
            userId, executor, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task WarnUserAsync_NotificationFails_StillReturnsSuccess()
    {
        // Arrange - Warning succeeds but notification delivery fails
        const long userId = 12345L;
        var executor = Actor.FromSystem("SpamDetection");

        _mockWarnHandler.WarnAsync(userId, executor, Arg.Any<string>(), Arg.Any<long>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(WarnResult.Succeeded(warningCount: 1));

        _mockConfigService.GetEffectiveAsync<WarningSystemConfig>(
                ConfigType.Moderation, Arg.Any<long>())
            .Returns(WarningSystemConfig.Default);

        _mockNotificationHandler.NotifyUserWarningAsync(userId, 1, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(NotificationResult.Failed("User blocked bot"));

        // Act
        var result = await _orchestrator.WarnUserAsync(userId, null, executor, "Spam detected", TestChatId);

        // Assert - Overall success (warning recorded), notification failure is non-critical
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.WarningCount, Is.EqualTo(1));
        });

        // Verify: Audit still logged despite notification failure
        await _mockAuditHandler.Received(1).LogWarnAsync(
            userId, executor, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task WarnUserAsync_AutoBanFails_ReturnsSuccessWithWarningButNotBan()
    {
        // Arrange - Warning succeeds, threshold reached, but auto-ban fails
        const long userId = 12345L;
        var executor = Actor.FromSystem("SpamDetection");

        _mockWarnHandler.WarnAsync(userId, executor, Arg.Any<string>(), Arg.Any<long>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(WarnResult.Succeeded(warningCount: 3)); // Reaches threshold

        _mockConfigService.GetEffectiveAsync<WarningSystemConfig>(
                ConfigType.Moderation, Arg.Any<long>())
            .Returns(new WarningSystemConfig { AutoBanEnabled = true, AutoBanThreshold = 3 });

        _mockBanHandler.BanAsync(Arg.Any<long>(), Arg.Any<Actor>(), Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(BanResult.Failed("API rate limited"));

        // Act
        var result = await _orchestrator.WarnUserAsync(userId, null, executor, "Spam detected", TestChatId);

        // Assert - Warning succeeded, auto-ban attempted but failed
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, "Warning itself succeeded");
            Assert.That(result.WarningCount, Is.EqualTo(3));
            Assert.That(result.AutoBanTriggered, Is.False, "Auto-ban was attempted but failed");
        });

        // Verify: Ban was attempted
        await _mockBanHandler.Received(1).BanAsync(
            userId, Actor.AutoBan, Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UnbanUserAsync_TrustRestorationFails_StillReturnsSuccess()
    {
        // Arrange - Unban succeeds but trust restoration fails
        const long userId = 12345L;
        var executor = Actor.FromWebUser("web-user", "admin@test.com");

        _mockBanHandler.UnbanAsync(userId, executor, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(UnbanResult.Succeeded(chatsAffected: 5, chatsFailed: 0));

        _mockTrustHandler.TrustAsync(userId, executor, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TrustResult.Failed("User already trusted"));

        // Act
        var result = await _orchestrator.UnbanUserAsync(
            userId, executor, "False positive", restoreTrust: true);

        // Assert - Unban succeeded, trust restoration failed
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.ChatsAffected, Is.EqualTo(5));
            Assert.That(result.TrustRestored, Is.False, "Trust restoration failed");
        });
    }

    [Test]
    public async Task MarkAsSpamAndBanAsync_TrainingDataFails_StillReturnsSuccess()
    {
        // Arrange - All actions succeed except training data creation
        const long messageId = 42L;
        const long userId = 12345L;
        const long chatId = -100123456789L;
        var executor = Actor.FromSystem("SpamDetection");

        _mockMessageHandler.EnsureExistsAsync(messageId, chatId, Arg.Any<global::Telegram.Bot.Types.Message?>(), Arg.Any<CancellationToken>())
            .Returns(BackfillResult.AlreadyExists());

        _mockMessageHandler.DeleteAsync(chatId, messageId, executor, Arg.Any<CancellationToken>())
            .Returns(DeleteResult.Succeeded(messageDeleted: true));

        _mockBanHandler.BanAsync(userId, executor, Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(BanResult.Succeeded(chatsAffected: 5, chatsFailed: 0));

        _mockTrustHandler.UntrustAsync(userId, executor, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(UntrustResult.Succeeded());

        _mockTrainingHandler.CreateSpamSampleAsync(messageId, executor, Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _orchestrator.MarkAsSpamAndBanAsync(
            messageId, userId, chatId, executor, "Spam detected");

        // Assert - Overall success (ban is the critical action)
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.MessageDeleted, Is.True);
            Assert.That(result.ChatsAffected, Is.EqualTo(5));
        });

        // Verify training data creation was attempted
        await _mockTrainingHandler.Received(1).CreateSpamSampleAsync(
            messageId, executor, Arg.Any<CancellationToken>());
    }

    #endregion
}
