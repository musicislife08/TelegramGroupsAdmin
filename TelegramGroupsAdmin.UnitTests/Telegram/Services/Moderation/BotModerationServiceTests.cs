using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Core;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Results;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Handlers;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.Moderation;

/// <summary>
/// Unit tests for BotModerationService.
/// Tests business rules (bans revoke trust, N warnings = auto-ban) and workflow composition.
/// The orchestrator is the "boss" that coordinates handlers and owns business rules.
/// </summary>
[TestFixture]
public class BotModerationServiceTests
{
    private const long TestChatId = 123456789L;
    private IBotBanHandler _mockBanHandler = null!;
    private ITrustHandler _mockTrustHandler = null!;
    private IWarnHandler _mockWarnHandler = null!;
    private IBotModerationMessageHandler _mockMessageHandler = null!;
    private IBotRestrictHandler _mockRestrictHandler = null!;
    private IAuditHandler _mockAuditHandler = null!;
    private INotificationHandler _mockNotificationHandler = null!;
    private ITrainingHandler _mockTrainingHandler = null!;
    private IBanCelebrationService _mockBanCelebrationService = null!;
    private IReportService _mockReportService = null!;
    private INotificationService _mockNotificationService = null!;
    private IConfigService _mockConfigService = null!;
    private ILogger<BotModerationService> _mockLogger = null!;
    private BotModerationService _orchestrator = null!;

    [SetUp]
    public void SetUp()
    {
        _mockBanHandler = Substitute.For<IBotBanHandler>();
        _mockTrustHandler = Substitute.For<ITrustHandler>();
        _mockWarnHandler = Substitute.For<IWarnHandler>();
        _mockMessageHandler = Substitute.For<IBotModerationMessageHandler>();
        _mockRestrictHandler = Substitute.For<IBotRestrictHandler>();
        _mockAuditHandler = Substitute.For<IAuditHandler>();
        _mockNotificationHandler = Substitute.For<INotificationHandler>();
        _mockTrainingHandler = Substitute.For<ITrainingHandler>();
        _mockBanCelebrationService = Substitute.For<IBanCelebrationService>();
        _mockReportService = Substitute.For<IReportService>();
        _mockNotificationService = Substitute.For<INotificationService>();
        _mockConfigService = Substitute.For<IConfigService>();
        _mockLogger = Substitute.For<ILogger<BotModerationService>>();

        _orchestrator = new BotModerationService(
            _mockBanHandler,
            _mockTrustHandler,
            _mockWarnHandler,
            _mockMessageHandler,
            _mockRestrictHandler,
            _mockAuditHandler,
            _mockNotificationHandler,
            _mockTrainingHandler,
            _mockBanCelebrationService,
            _mockReportService,
            _mockNotificationService,
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
            new BanIntent
            {
                User = UserIdentity.FromId(systemUserId),
                Executor = Actor.FromSystem("test"),
                Reason = "Attempted ban of system account"
            });

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("system account"));
        });

        // Verify no handler was called
        await _mockBanHandler.DidNotReceive().BanAsync(
            Arg.Any<UserIdentity>(),
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
            new WarnIntent
            {
                User = UserIdentity.FromId(systemUserId),
                Executor = Actor.FromSystem("test"),
                Reason = "Attempted warning of system account",
                Chat = ChatIdentity.FromId(TestChatId)
            });

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
            new TempBanIntent
            {
                User = UserIdentity.FromId(systemUserId),
                Executor = Actor.FromSystem("test"),
                Reason = "Test",
                Duration = TimeSpan.FromHours(1)
            });

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

        _mockBanHandler.BanAsync(Arg.Any<UserIdentity>(), executor, Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(BanResult.Succeeded(chatsAffected: 5, chatsFailed: 0));

        _mockTrustHandler.UntrustAsync(Arg.Any<UserIdentity>(), executor, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(UntrustResult.Succeeded());

        // Act
        var result = await _orchestrator.BanUserAsync(
            new BanIntent
            {
                User = UserIdentity.FromId(userId),
                Executor = executor,
                Reason = "Spam violation"
            });

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.ChatsAffected, Is.EqualTo(5));
            Assert.That(result.TrustRemoved, Is.True);
        });

        // Verify business rule: bans always revoke trust
        await _mockTrustHandler.Received(1).UntrustAsync(
            Arg.Is<UserIdentity>(u => u.Id == userId),
            executor,
            Arg.Is<string>(s => s!.Contains("ban")),
            Arg.Any<CancellationToken>());

        // Verify audit was logged
        await _mockAuditHandler.Received(1).LogBanAsync(
            Arg.Is<UserIdentity>(u => u.Id == userId), executor, Arg.Any<string>(), Arg.Any<CancellationToken>());

        // Verify admins were notified
        await _mockNotificationHandler.Received(1).NotifyAdminsBanAsync(
            Arg.Is<UserIdentity>(u => u.Id == userId), executor, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task BanUserAsync_BanFails_DoesNotRevokeTrust()
    {
        // Arrange
        const long userId = 12345L;
        var executor = Actor.FromSystem("test");

        _mockBanHandler.BanAsync(Arg.Any<UserIdentity>(), executor, Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(BanResult.Failed("API error"));

        // Act
        var result = await _orchestrator.BanUserAsync(
            new BanIntent
            {
                User = UserIdentity.FromId(userId),
                Executor = executor,
                Reason = "Test"
            });

        // Assert
        Assert.That(result.Success, Is.False);

        // Verify trust was NOT revoked (ban failed)
        await _mockTrustHandler.DidNotReceive().UntrustAsync(
            Arg.Any<UserIdentity>(),
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

        _mockWarnHandler.WarnAsync(Arg.Any<UserIdentity>(), executor, Arg.Any<string>(), Arg.Any<long>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(WarnResult.Succeeded(warningCount: 2)); // Below threshold

        _mockConfigService.GetEffectiveAsync<WarningSystemConfig>(
                ConfigType.Moderation, Arg.Any<long>())
            .Returns(new WarningSystemConfig { AutoBanEnabled = true, AutoBanThreshold = 3 });

        // Act
        var result = await _orchestrator.WarnUserAsync(
            new WarnIntent
            {
                User = UserIdentity.FromId(userId),
                Executor = executor,
                Reason = "First warning",
                Chat = ChatIdentity.FromId(TestChatId)
            });

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.WarningCount, Is.EqualTo(2));
            Assert.That(result.AutoBanTriggered, Is.False);
        });

        // Verify ban was NOT called
        await _mockBanHandler.DidNotReceive().BanAsync(
            Arg.Any<UserIdentity>(),
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

        _mockWarnHandler.WarnAsync(Arg.Any<UserIdentity>(), executor, Arg.Any<string>(), Arg.Any<long>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(WarnResult.Succeeded(warningCount: 3)); // Reaches threshold

        _mockConfigService.GetEffectiveAsync<WarningSystemConfig>(
                ConfigType.Moderation, Arg.Any<long>())
            .Returns(new WarningSystemConfig { AutoBanEnabled = true, AutoBanThreshold = 3 });

        _mockBanHandler.BanAsync(Arg.Any<UserIdentity>(), Arg.Any<Actor>(), Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(BanResult.Succeeded(chatsAffected: 5, chatsFailed: 0));

        _mockTrustHandler.UntrustAsync(Arg.Any<UserIdentity>(), Arg.Any<Actor>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(UntrustResult.Succeeded());

        // Act
        var result = await _orchestrator.WarnUserAsync(
            new WarnIntent
            {
                User = UserIdentity.FromId(userId),
                Executor = executor,
                Reason = "Final warning",
                Chat = ChatIdentity.FromId(TestChatId)
            });

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
            Arg.Is<UserIdentity>(u => u.Id == userId),
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

        _mockWarnHandler.WarnAsync(Arg.Any<UserIdentity>(), executor, Arg.Any<string>(), Arg.Any<long>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(WarnResult.Succeeded(warningCount: 5)); // Well above threshold

        _mockConfigService.GetEffectiveAsync<WarningSystemConfig>(
                ConfigType.Moderation, Arg.Any<long>())
            .Returns(new WarningSystemConfig { AutoBanEnabled = false, AutoBanThreshold = 3 });

        // Act
        var result = await _orchestrator.WarnUserAsync(
            new WarnIntent
            {
                User = UserIdentity.FromId(userId),
                Executor = executor,
                Reason = "Warning",
                Chat = ChatIdentity.FromId(TestChatId)
            });

        // Assert
        Assert.That(result.AutoBanTriggered, Is.False);

        // Verify ban was NOT called even though count exceeds threshold
        await _mockBanHandler.DidNotReceive().BanAsync(
            Arg.Any<UserIdentity>(),
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

        _mockWarnHandler.WarnAsync(Arg.Any<UserIdentity>(), executor, Arg.Any<string>(), Arg.Any<long>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(WarnResult.Succeeded(warningCount: 1));

        _mockConfigService.GetEffectiveAsync<WarningSystemConfig>(
                ConfigType.Moderation, Arg.Any<long>())
            .Returns(WarningSystemConfig.Default);

        // Act
        var result = await _orchestrator.WarnUserAsync(
            new WarnIntent
            {
                User = UserIdentity.FromId(userId),
                Executor = executor,
                Reason = "Spam detected",
                Chat = ChatIdentity.FromId(TestChatId)
            });

        // Assert
        Assert.That(result.Success, Is.True);

        // Verify user was notified about their warning
        await _mockNotificationHandler.Received(1).NotifyUserWarningAsync(
            Arg.Is<UserIdentity>(u => u.Id == userId),
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

        _mockMessageHandler.EnsureExistsAsync(messageId, Arg.Any<ChatIdentity>(), Arg.Any<global::Telegram.Bot.Types.Message?>(), Arg.Any<CancellationToken>())
            .Returns(BackfillResult.AlreadyExists());

        _mockMessageHandler.DeleteAsync(Arg.Any<ChatIdentity>(), messageId, executor, Arg.Any<CancellationToken>())
            .Returns(DeleteResult.Succeeded(messageDeleted: true));

        _mockBanHandler.BanAsync(Arg.Any<UserIdentity>(), executor, Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(BanResult.Succeeded(chatsAffected: 5, chatsFailed: 0));

        _mockTrustHandler.UntrustAsync(Arg.Any<UserIdentity>(), executor, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(UntrustResult.Succeeded());

        // Act
        var result = await _orchestrator.MarkAsSpamAndBanAsync(
            new SpamBanIntent
            {
                User = UserIdentity.FromId(userId),
                MessageId = messageId,
                Chat = ChatIdentity.FromId(chatId),
                Executor = executor,
                Reason = "Spam detected"
            });

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
            messageId, Arg.Any<ChatIdentity>(), Arg.Any<global::Telegram.Bot.Types.Message?>(), Arg.Any<CancellationToken>());

        await _mockMessageHandler.Received(1).DeleteAsync(
            Arg.Any<ChatIdentity>(), messageId, executor, Arg.Any<CancellationToken>());

        await _mockBanHandler.Received(1).BanAsync(
            Arg.Is<UserIdentity>(u => u.Id == userId), executor, Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>());

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

        _mockMessageHandler.EnsureExistsAsync(messageId, Arg.Any<ChatIdentity>(), Arg.Any<global::Telegram.Bot.Types.Message?>(), Arg.Any<CancellationToken>())
            .Returns(BackfillResult.AlreadyExists());

        _mockMessageHandler.DeleteAsync(Arg.Any<ChatIdentity>(), messageId, executor, Arg.Any<CancellationToken>())
            .Returns(DeleteResult.Succeeded(messageDeleted: false)); // Deletion failed (soft failure)

        _mockBanHandler.BanAsync(Arg.Any<UserIdentity>(), executor, Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(BanResult.Succeeded(chatsAffected: 5, chatsFailed: 0));

        _mockTrustHandler.UntrustAsync(Arg.Any<UserIdentity>(), executor, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(UntrustResult.Succeeded());

        // Act
        var result = await _orchestrator.MarkAsSpamAndBanAsync(
            new SpamBanIntent
            {
                User = UserIdentity.FromId(userId),
                MessageId = messageId,
                Chat = ChatIdentity.FromId(chatId),
                Executor = executor,
                Reason = "Spam detected"
            });

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

        _mockMessageHandler.EnsureExistsAsync(messageId, Arg.Any<ChatIdentity>(), Arg.Any<global::Telegram.Bot.Types.Message?>(), Arg.Any<CancellationToken>())
            .Returns(BackfillResult.AlreadyExists());

        _mockMessageHandler.DeleteAsync(Arg.Any<ChatIdentity>(), messageId, executor, Arg.Any<CancellationToken>())
            .Returns(DeleteResult.Succeeded(messageDeleted: true));

        _mockBanHandler.BanAsync(Arg.Any<UserIdentity>(), executor, Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(BanResult.Failed("API error"));

        // Act
        var result = await _orchestrator.MarkAsSpamAndBanAsync(
            new SpamBanIntent
            {
                User = UserIdentity.FromId(userId),
                MessageId = messageId,
                Chat = ChatIdentity.FromId(chatId),
                Executor = executor,
                Reason = "Spam detected"
            });

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

        _mockTrustHandler.TrustAsync(Arg.Any<UserIdentity>(), executor, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TrustResult.Succeeded());

        // Act
        var result = await _orchestrator.TrustUserAsync(
            new TrustIntent
            {
                User = UserIdentity.FromId(userId),
                Executor = executor,
                Reason = "Verified user"
            });

        // Assert
        Assert.That(result.Success, Is.True);

        // Verify audit was logged
        await _mockAuditHandler.Received(1).LogTrustAsync(
            Arg.Is<UserIdentity>(u => u.Id == userId), executor, "Verified user", Arg.Any<CancellationToken>());
    }

    #endregion

    #region UnbanUserAsync Tests

    [Test]
    public async Task UnbanUserAsync_WithRestoreTrust_RestoresTrust()
    {
        // Arrange
        const long userId = 12345L;
        var executor = Actor.FromWebUser("web-user", "admin@test.com");

        _mockBanHandler.UnbanAsync(Arg.Any<UserIdentity>(), executor, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(UnbanResult.Succeeded(chatsAffected: 5, chatsFailed: 0));

        _mockTrustHandler.TrustAsync(Arg.Any<UserIdentity>(), executor, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TrustResult.Succeeded());

        // Act
        var result = await _orchestrator.UnbanUserAsync(
            new UnbanIntent
            {
                User = UserIdentity.FromId(userId),
                Executor = executor,
                Reason = "False positive",
                RestoreTrust = true
            });

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.ChatsAffected, Is.EqualTo(5));
            Assert.That(result.TrustRestored, Is.True);
        });

        // Verify trust was restored
        await _mockTrustHandler.Received(1).TrustAsync(
            Arg.Is<UserIdentity>(u => u.Id == userId), executor, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UnbanUserAsync_WithoutRestoreTrust_DoesNotRestoreTrust()
    {
        // Arrange
        const long userId = 12345L;
        var executor = Actor.FromWebUser("web-user", "admin@test.com");

        _mockBanHandler.UnbanAsync(Arg.Any<UserIdentity>(), executor, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(UnbanResult.Succeeded(chatsAffected: 5, chatsFailed: 0));

        // Act
        var result = await _orchestrator.UnbanUserAsync(
            new UnbanIntent
            {
                User = UserIdentity.FromId(userId),
                Executor = executor,
                Reason = "Ban expired",
                RestoreTrust = false
            });

        // Assert
        Assert.That(result.TrustRestored, Is.False);

        // Verify trust was NOT restored
        await _mockTrustHandler.DidNotReceive().TrustAsync(
            Arg.Any<UserIdentity>(),
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

        _mockBanHandler.TempBanAsync(Arg.Any<UserIdentity>(), executor, duration, Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(TempBanResult.Succeeded(chatsAffected: 5, expiresAt, chatsFailed: 0));

        // Act
        var result = await _orchestrator.TempBanUserAsync(
            new TempBanIntent
            {
                User = UserIdentity.FromId(userId),
                Executor = executor,
                Reason = "Temporary mute for spam",
                Duration = duration
            });

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.ChatsAffected, Is.EqualTo(5));
        });

        // Verify user was notified about temp ban
        await _mockNotificationHandler.Received(1).NotifyUserTempBanAsync(
            Arg.Is<UserIdentity>(u => u.Id == userId),
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

        _mockBanHandler.TempBanAsync(Arg.Any<UserIdentity>(), executor, duration, Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(TempBanResult.Failed("API error"));

        // Act
        var result = await _orchestrator.TempBanUserAsync(
            new TempBanIntent
            {
                User = UserIdentity.FromId(userId),
                Executor = executor,
                Reason = "Test",
                Duration = duration
            });

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("API error"));
        });

        // Verify notification was NOT sent on failure
        await _mockNotificationHandler.DidNotReceive().NotifyUserTempBanAsync(
            Arg.Any<UserIdentity>(),
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

        _mockBanHandler.TempBanAsync(Arg.Any<UserIdentity>(), executor, duration, Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(TempBanResult.Succeeded(chatsAffected: 3, expiresAt, chatsFailed: 2));

        // Act
        var result = await _orchestrator.TempBanUserAsync(
            new TempBanIntent
            {
                User = UserIdentity.FromId(userId),
                Executor = executor,
                Reason = "Spam warning",
                Duration = duration
            });

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.ChatsAffected, Is.EqualTo(3));
        });

        // Verify user was notified even with partial success
        await _mockNotificationHandler.Received(1).NotifyUserTempBanAsync(
            Arg.Is<UserIdentity>(u => u.Id == userId),
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
            new RestrictIntent
            {
                User = UserIdentity.FromId(systemUserId),
                Executor = Actor.FromSystem("test"),
                Reason = "Test",
                Duration = TimeSpan.FromHours(1)
            });

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("system account"));
        });

        // Verify no handler was called
        await _mockRestrictHandler.DidNotReceive().RestrictAsync(
            Arg.Any<UserIdentity>(),
            Arg.Any<ChatIdentity?>(),
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

        _mockRestrictHandler.RestrictAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity?>(), executor, duration, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(RestrictResult.Succeeded(chatsAffected: 1, expiresAt, chatsFailed: 0));

        // Act
        var result = await _orchestrator.RestrictUserAsync(
            new RestrictIntent
            {
                User = UserIdentity.FromId(userId),
                Executor = executor,
                Reason = "Welcome mute",
                Duration = duration,
                Chat = ChatIdentity.FromId(chatId)
            });

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.ChatsAffected, Is.EqualTo(1));
        });

        // Verify audit was logged
        await _mockAuditHandler.Received(1).LogRestrictAsync(
            Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity?>(), executor, "Welcome mute", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RestrictUserAsync_GlobalRestrict_UsesZeroChatId()
    {
        // Arrange - chatId=null means global restriction
        const long userId = 12345L;
        var executor = Actor.FromSystem("SpamDetection");
        var duration = TimeSpan.FromHours(1);
        var expiresAt = DateTimeOffset.UtcNow.Add(duration);

        _mockRestrictHandler.RestrictAsync(Arg.Any<UserIdentity>(), null, executor, duration, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(RestrictResult.Succeeded(chatsAffected: 5, expiresAt, chatsFailed: 0));

        // Act
        var result = await _orchestrator.RestrictUserAsync(
            new RestrictIntent
            {
                User = UserIdentity.FromId(userId),
                Executor = executor,
                Reason = "Global mute",
                Duration = duration,
                Chat = null // Global restriction
            });

        // Assert
        Assert.That(result.Success, Is.True);

        // Verify RestrictHandler was called with null chat (global)
        await _mockRestrictHandler.Received(1).RestrictAsync(
            Arg.Any<UserIdentity>(),
            null, // Global
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

        _mockRestrictHandler.RestrictAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity?>(), executor, Arg.Any<TimeSpan>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(RestrictResult.Failed("User is admin"));

        // Act
        var result = await _orchestrator.RestrictUserAsync(
            new RestrictIntent
            {
                User = UserIdentity.FromId(userId),
                Executor = executor,
                Reason = "Test",
                Duration = TimeSpan.FromHours(1),
                Chat = ChatIdentity.FromId(chatId)
            });

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("User is admin"));
        });

        // Verify audit was NOT logged on failure
        await _mockAuditHandler.DidNotReceive().LogRestrictAsync(
            Arg.Any<UserIdentity>(),
            Arg.Any<ChatIdentity?>(),
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

        _mockMessageHandler.DeleteAsync(Arg.Any<ChatIdentity>(), messageId, executor, Arg.Any<CancellationToken>())
            .Returns(DeleteResult.Succeeded(messageDeleted: true));

        // Act
        var result = await _orchestrator.DeleteMessageAsync(
            new DeleteMessageIntent
            {
                User = UserIdentity.FromId(userId),
                MessageId = messageId,
                Chat = ChatIdentity.FromId(chatId),
                Executor = executor,
                Reason = "Spam"
            });

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.MessageDeleted, Is.True);
        });

        // Verify audit was logged
        await _mockAuditHandler.Received(1).LogDeleteAsync(
            messageId, Arg.Any<ChatIdentity>(), Arg.Any<UserIdentity>(), executor, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeleteMessageAsync_MessageAlreadyDeleted_StillAudits()
    {
        // Arrange - Message was already deleted (returns success but messageDeleted=false)
        const long messageId = 42L;
        const long chatId = -100123456789L;
        const long userId = 12345L;
        var executor = Actor.FromTelegramUser(999, "Admin");

        _mockMessageHandler.DeleteAsync(Arg.Any<ChatIdentity>(), messageId, executor, Arg.Any<CancellationToken>())
            .Returns(DeleteResult.Succeeded(messageDeleted: false));

        // Act
        var result = await _orchestrator.DeleteMessageAsync(
            new DeleteMessageIntent
            {
                User = UserIdentity.FromId(userId),
                MessageId = messageId,
                Chat = ChatIdentity.FromId(chatId),
                Executor = executor,
                Reason = "Spam"
            });

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.MessageDeleted, Is.False);
        });

        // Verify audit was still logged (deletion attempt recorded)
        await _mockAuditHandler.Received(1).LogDeleteAsync(
            messageId, Arg.Any<ChatIdentity>(), Arg.Any<UserIdentity>(), executor, Arg.Any<CancellationToken>());
    }

    #endregion

    #region Business Rule Failure Recovery Tests

    [Test]
    public async Task BanUserAsync_TrustRevocationFails_StillReturnsSuccess()
    {
        // Arrange - Ban succeeds but trust revocation fails
        const long userId = 12345L;
        var executor = Actor.FromSystem("SpamDetection");

        _mockBanHandler.BanAsync(Arg.Any<UserIdentity>(), executor, Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(BanResult.Succeeded(chatsAffected: 5, chatsFailed: 0));

        _mockTrustHandler.UntrustAsync(Arg.Any<UserIdentity>(), executor, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(UntrustResult.Failed("Database error"));

        // Act
        var result = await _orchestrator.BanUserAsync(
            new BanIntent
            {
                User = UserIdentity.FromId(userId),
                Executor = executor,
                Reason = "Spam violation"
            });

        // Assert - Overall success (ban completed), but TrustRemoved=false
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.ChatsAffected, Is.EqualTo(5));
            Assert.That(result.TrustRemoved, Is.False, "Trust revocation failed, so should be false");
        });

        // Verify: Ban audit logged, but Untrust audit NOT logged (it failed)
        await _mockAuditHandler.Received(1).LogBanAsync(
            Arg.Any<UserIdentity>(), executor, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _mockAuditHandler.DidNotReceive().LogUntrustAsync(
            Arg.Any<UserIdentity>(), Arg.Any<Actor>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        // Verify: Admins still notified (non-critical failure doesn't block notification)
        await _mockNotificationHandler.Received(1).NotifyAdminsBanAsync(
            Arg.Any<UserIdentity>(), executor, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task WarnUserAsync_NotificationFails_StillReturnsSuccess()
    {
        // Arrange - Warning succeeds but notification delivery fails
        const long userId = 12345L;
        var executor = Actor.FromSystem("SpamDetection");

        _mockWarnHandler.WarnAsync(Arg.Any<UserIdentity>(), executor, Arg.Any<string>(), Arg.Any<long>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(WarnResult.Succeeded(warningCount: 1));

        _mockConfigService.GetEffectiveAsync<WarningSystemConfig>(
                ConfigType.Moderation, Arg.Any<long>())
            .Returns(WarningSystemConfig.Default);

        _mockNotificationHandler.NotifyUserWarningAsync(Arg.Any<UserIdentity>(), 1, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(NotificationResult.Failed("User blocked bot"));

        // Act
        var result = await _orchestrator.WarnUserAsync(
            new WarnIntent
            {
                User = UserIdentity.FromId(userId),
                Executor = executor,
                Reason = "Spam detected",
                Chat = ChatIdentity.FromId(TestChatId)
            });

        // Assert - Overall success (warning recorded), notification failure is non-critical
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.WarningCount, Is.EqualTo(1));
        });

        // Verify: Audit still logged despite notification failure
        await _mockAuditHandler.Received(1).LogWarnAsync(
            Arg.Any<UserIdentity>(), executor, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task WarnUserAsync_AutoBanFails_ReturnsSuccessWithWarningButNotBan()
    {
        // Arrange - Warning succeeds, threshold reached, but auto-ban fails
        const long userId = 12345L;
        var executor = Actor.FromSystem("SpamDetection");

        _mockWarnHandler.WarnAsync(Arg.Any<UserIdentity>(), executor, Arg.Any<string>(), Arg.Any<long>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(WarnResult.Succeeded(warningCount: 3)); // Reaches threshold

        _mockConfigService.GetEffectiveAsync<WarningSystemConfig>(
                ConfigType.Moderation, Arg.Any<long>())
            .Returns(new WarningSystemConfig { AutoBanEnabled = true, AutoBanThreshold = 3 });

        _mockBanHandler.BanAsync(Arg.Any<UserIdentity>(), Arg.Any<Actor>(), Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(BanResult.Failed("API rate limited"));

        // Act
        var result = await _orchestrator.WarnUserAsync(
            new WarnIntent
            {
                User = UserIdentity.FromId(userId),
                Executor = executor,
                Reason = "Spam detected",
                Chat = ChatIdentity.FromId(TestChatId)
            });

        // Assert - Warning succeeded, auto-ban attempted but failed
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, "Warning itself succeeded");
            Assert.That(result.WarningCount, Is.EqualTo(3));
            Assert.That(result.AutoBanTriggered, Is.False, "Auto-ban was attempted but failed");
        });

        // Verify: Ban was attempted
        await _mockBanHandler.Received(1).BanAsync(
            Arg.Is<UserIdentity>(u => u.Id == userId), Actor.AutoBan, Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UnbanUserAsync_TrustRestorationFails_StillReturnsSuccess()
    {
        // Arrange - Unban succeeds but trust restoration fails
        const long userId = 12345L;
        var executor = Actor.FromWebUser("web-user", "admin@test.com");

        _mockBanHandler.UnbanAsync(Arg.Any<UserIdentity>(), executor, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(UnbanResult.Succeeded(chatsAffected: 5, chatsFailed: 0));

        _mockTrustHandler.TrustAsync(Arg.Any<UserIdentity>(), executor, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TrustResult.Failed("User already trusted"));

        // Act
        var result = await _orchestrator.UnbanUserAsync(
            new UnbanIntent
            {
                User = UserIdentity.FromId(userId),
                Executor = executor,
                Reason = "False positive",
                RestoreTrust = true
            });

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

        _mockMessageHandler.EnsureExistsAsync(messageId, Arg.Any<ChatIdentity>(), Arg.Any<global::Telegram.Bot.Types.Message?>(), Arg.Any<CancellationToken>())
            .Returns(BackfillResult.AlreadyExists());

        _mockMessageHandler.DeleteAsync(Arg.Any<ChatIdentity>(), messageId, executor, Arg.Any<CancellationToken>())
            .Returns(DeleteResult.Succeeded(messageDeleted: true));

        _mockBanHandler.BanAsync(Arg.Any<UserIdentity>(), executor, Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(BanResult.Succeeded(chatsAffected: 5, chatsFailed: 0));

        _mockTrustHandler.UntrustAsync(Arg.Any<UserIdentity>(), executor, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(UntrustResult.Succeeded());

        _mockTrainingHandler.CreateSpamSampleAsync(messageId, executor, Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _orchestrator.MarkAsSpamAndBanAsync(
            new SpamBanIntent
            {
                User = UserIdentity.FromId(userId),
                MessageId = messageId,
                Chat = ChatIdentity.FromId(chatId),
                Executor = executor,
                Reason = "Spam detected"
            });

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

    #region SyncBanToChatAsync Tests

    [Test]
    public async Task SyncBanToChatAsync_SuccessfulSync_ReturnsSingleChatAffected()
    {
        // Arrange
        _mockBanHandler.BanInChatAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Actor.AutoDetection, Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(BanResult.Succeeded(chatsAffected: 1));

        // Act
        var result = await _orchestrator.SyncBanToChatAsync(
            new SyncBanIntent
            {
                User = UserIdentity.FromId(12345),
                Chat = ChatIdentity.FromId(-100123456789),
                Executor = Actor.AutoDetection,
                Reason = "Lazy ban sync",
                TriggeredByMessageId = 42L
            });

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.ChatsAffected, Is.EqualTo(1));
        });

        // Verify audit record was created
        await _mockAuditHandler.Received(1).LogBanAsync(
            Arg.Is<UserIdentity>(u => u.Id == 12345), Actor.AutoDetection, "Lazy ban sync", Arg.Any<CancellationToken>());
    }

    [Test]
    [TestCase(777000, Description = "Service account")]
    [TestCase(1087968824, Description = "Anonymous admin bot")]
    public async Task SyncBanToChatAsync_TelegramSystemAccount_ReturnsError(long systemUserId)
    {
        // Arrange + Act
        var result = await _orchestrator.SyncBanToChatAsync(
            new SyncBanIntent
            {
                User = UserIdentity.FromId(systemUserId),
                Chat = ChatIdentity.FromId(-100123456789),
                Executor = Actor.AutoDetection,
                Reason = "Test"
            });

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("system account"));
        });

        // Verify handler was NOT called
        await _mockBanHandler.DidNotReceive().BanInChatAsync(
            Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<Actor>(),
            Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SyncBanToChatAsync_BanFails_ReturnsFailure()
    {
        // Arrange
        _mockBanHandler.BanInChatAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Actor.AutoDetection, Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(BanResult.Failed("User is chat admin"));

        // Act
        var result = await _orchestrator.SyncBanToChatAsync(
            new SyncBanIntent
            {
                User = UserIdentity.FromId(12345),
                Chat = ChatIdentity.FromId(-100123456789),
                Executor = Actor.AutoDetection,
                Reason = "Lazy sync"
            });

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("User is chat admin"));
        });

        // Verify no audit record on failure
        await _mockAuditHandler.DidNotReceive().LogBanAsync(
            Arg.Any<UserIdentity>(), Arg.Any<Actor>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SyncBanToChatAsync_UsesAutoDetectionActor()
    {
        // Arrange - Verify the intent's executor flows through to the handler
        _mockBanHandler.BanInChatAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<Actor>(), Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(BanResult.Succeeded(chatsAffected: 1));

        // Act
        await _orchestrator.SyncBanToChatAsync(
            new SyncBanIntent
            {
                User = UserIdentity.FromId(12345),
                Chat = ChatIdentity.FromId(-100123456789),
                Executor = Actor.AutoDetection,
                Reason = "Lazy sync",
                TriggeredByMessageId = 42L
            });

        // Assert - Verify Actor.AutoDetection was forwarded to handler
        await _mockBanHandler.Received(1).BanInChatAsync(
            Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Actor.AutoDetection, "Lazy sync", 42L, Arg.Any<CancellationToken>());
    }

    #endregion

    #region SafeAuditAsync Tests - Audit Failures Don't Block Operations

    [Test]
    public async Task DeleteMessageAsync_AuditFails_StillReturnsSuccess()
    {
        // Arrange - Message deletion succeeds but audit logging throws
        const long messageId = 42L;
        const long chatId = -100123456789L;
        const long userId = 12345L;
        var executor = Actor.FromTelegramUser(999, "Admin");

        _mockMessageHandler.DeleteAsync(Arg.Any<ChatIdentity>(), messageId, executor, Arg.Any<CancellationToken>())
            .Returns(DeleteResult.Succeeded(messageDeleted: true));

        _mockAuditHandler.LogDeleteAsync(messageId, Arg.Any<ChatIdentity>(), Arg.Any<UserIdentity>(), executor, Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("FK constraint violation")); // Simulates Bug 1

        // Act
        var result = await _orchestrator.DeleteMessageAsync(
            new DeleteMessageIntent
            {
                User = UserIdentity.FromId(userId),
                MessageId = messageId,
                Chat = ChatIdentity.FromId(chatId),
                Executor = executor,
                Reason = "Spam"
            });

        // Assert - Primary operation succeeded despite audit failure
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, "Telegram operation succeeded, so result should be success");
            Assert.That(result.MessageDeleted, Is.True);
        });

        // Verify audit was attempted (but failed gracefully)
        await _mockAuditHandler.Received(1).LogDeleteAsync(
            messageId, Arg.Any<ChatIdentity>(), Arg.Any<UserIdentity>(), executor, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task BanUserAsync_AuditFails_StillReturnsSuccess()
    {
        // Arrange - Ban succeeds but audit logging throws
        const long userId = 12345L;
        var executor = Actor.FromSystem("SpamDetection");

        _mockBanHandler.BanAsync(Arg.Any<UserIdentity>(), executor, Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(BanResult.Succeeded(chatsAffected: 5, chatsFailed: 0));

        _mockTrustHandler.UntrustAsync(Arg.Any<UserIdentity>(), executor, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(UntrustResult.Succeeded());

        _mockAuditHandler.LogBanAsync(Arg.Any<UserIdentity>(), executor, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Database connection failed"));

        // Act
        var result = await _orchestrator.BanUserAsync(
            new BanIntent
            {
                User = UserIdentity.FromId(userId),
                Executor = executor,
                Reason = "Spam violation"
            });

        // Assert - Primary operation succeeded despite audit failure
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.ChatsAffected, Is.EqualTo(5));
        });

        // Verify audit was attempted
        await _mockAuditHandler.Received(1).LogBanAsync(
            Arg.Any<UserIdentity>(), executor, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task WarnUserAsync_AuditFails_StillReturnsSuccess()
    {
        // Arrange - Warning succeeds but audit logging throws
        const long userId = 12345L;
        var executor = Actor.FromSystem("SpamDetection");

        _mockWarnHandler.WarnAsync(Arg.Any<UserIdentity>(), executor, Arg.Any<string>(), Arg.Any<long>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(WarnResult.Succeeded(warningCount: 1));

        _mockConfigService.GetEffectiveAsync<WarningSystemConfig>(
                ConfigType.Moderation, Arg.Any<long>())
            .Returns(WarningSystemConfig.Default);

        _mockAuditHandler.LogWarnAsync(Arg.Any<UserIdentity>(), executor, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Database timeout"));

        // Act
        var result = await _orchestrator.WarnUserAsync(
            new WarnIntent
            {
                User = UserIdentity.FromId(userId),
                Executor = executor,
                Reason = "Spam detected",
                Chat = ChatIdentity.FromId(TestChatId)
            });

        // Assert - Primary operation succeeded despite audit failure
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.WarningCount, Is.EqualTo(1));
        });

        // Verify audit was attempted
        await _mockAuditHandler.Received(1).LogWarnAsync(
            Arg.Any<UserIdentity>(), executor, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RestoreUserPermissionsAsync_AuditFails_StillReturnsSuccess()
    {
        // Arrange - Permission restore succeeds but audit logging throws
        const long userId = 12345L;
        const long chatId = 67890L;
        var executor = Actor.FromSystem("ExamFlow");

        _mockRestrictHandler.RestorePermissionsAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), executor, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(RestrictResult.Succeeded(chatsAffected: 1, expiresAt: null, chatsFailed: 0));

        _mockAuditHandler.LogRestorePermissionsAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), executor, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Audit table locked"));

        // Act
        var result = await _orchestrator.RestoreUserPermissionsAsync(
            new RestorePermissionsIntent
            {
                User = UserIdentity.FromId(userId),
                Chat = ChatIdentity.FromId(chatId),
                Executor = executor,
                Reason = "Exam passed"
            });

        // Assert - Primary operation succeeded despite audit failure
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.ChatsAffected, Is.EqualTo(1));
        });

        // Verify audit was attempted
        await _mockAuditHandler.Received(1).LogRestorePermissionsAsync(
            Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), executor, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task KickUserFromChatAsync_AuditFails_StillReturnsSuccess()
    {
        // Arrange - Kick succeeds but audit logging throws
        const long userId = 12345L;
        const long chatId = -100123456789L;
        var executor = Actor.FromTelegramUser(999, "Admin");

        _mockBanHandler.KickFromChatAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), executor, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(BanResult.Succeeded(chatsAffected: 1, chatsFailed: 0));

        _mockAuditHandler.LogKickAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), executor, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Connection reset"));

        // Act
        var result = await _orchestrator.KickUserFromChatAsync(
            new KickIntent
            {
                User = UserIdentity.FromId(userId),
                Chat = ChatIdentity.FromId(chatId),
                Executor = executor,
                Reason = "Exam failed"
            });

        // Assert - Primary operation succeeded despite audit failure
        Assert.That(result.Success, Is.True);

        // Verify audit was attempted
        await _mockAuditHandler.Received(1).LogKickAsync(
            Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), executor, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region HandleMalwareViolationAsync Tests

    [Test]
    public async Task HandleMalwareViolationAsync_DeletesMessage()
    {
        // Arrange
        const long messageId = 1001L;
        const long chatId = 2002L;
        const long userId = 3003L;
        const string malwareDetails = "Trojan.GenericKD detected";

        _mockMessageHandler.EnsureExistsAsync(messageId, Arg.Any<ChatIdentity>(), null, Arg.Any<CancellationToken>())
            .Returns(BackfillResult.AlreadyExists());
        _mockMessageHandler.DeleteAsync(Arg.Any<ChatIdentity>(), messageId, Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(DeleteResult.Succeeded(messageDeleted: true));

        // Act
        var result = await _orchestrator.HandleMalwareViolationAsync(
            new MalwareViolationIntent
            {
                User = UserIdentity.FromId(userId),
                MessageId = messageId,
                Chat = ChatIdentity.FromId(chatId),
                MalwareDetails = malwareDetails,
                Executor = Actor.FileScanner,
                Reason = malwareDetails
            });

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.MessageDeleted, Is.True);

        // Verify message was deleted with FileScanner actor
        await _mockMessageHandler.Received(1).DeleteAsync(
            Arg.Any<ChatIdentity>(),
            messageId,
            Actor.FileScanner,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleMalwareViolationAsync_EnsuresMessageExists()
    {
        // Arrange
        const long messageId = 1001L;
        const long chatId = 2002L;
        const long userId = 3003L;
        const string malwareDetails = "Trojan detected";

        _mockMessageHandler.EnsureExistsAsync(messageId, Arg.Any<ChatIdentity>(), null, Arg.Any<CancellationToken>())
            .Returns(BackfillResult.Backfilled());
        _mockMessageHandler.DeleteAsync(Arg.Any<ChatIdentity>(), messageId, Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(DeleteResult.Succeeded(messageDeleted: true));

        // Act
        await _orchestrator.HandleMalwareViolationAsync(
            new MalwareViolationIntent
            {
                User = UserIdentity.FromId(userId),
                MessageId = messageId,
                Chat = ChatIdentity.FromId(chatId),
                MalwareDetails = malwareDetails,
                Executor = Actor.FileScanner,
                Reason = malwareDetails
            });

        // Assert - EnsureExistsAsync was called before delete
        await _mockMessageHandler.Received(1).EnsureExistsAsync(
            messageId,
            Arg.Any<ChatIdentity>(),
            null,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleMalwareViolationAsync_CreatesReport()
    {
        // Arrange
        const long messageId = 1001L;
        const long chatId = 2002L;
        const long userId = 3003L;
        const string malwareDetails = "Ransomware detected";

        _mockMessageHandler.EnsureExistsAsync(messageId, Arg.Any<ChatIdentity>(), null, Arg.Any<CancellationToken>())
            .Returns(BackfillResult.AlreadyExists());
        _mockMessageHandler.DeleteAsync(Arg.Any<ChatIdentity>(), messageId, Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(DeleteResult.Succeeded(messageDeleted: true));

        // Act
        await _orchestrator.HandleMalwareViolationAsync(
            new MalwareViolationIntent
            {
                User = UserIdentity.FromId(userId),
                MessageId = messageId,
                Chat = ChatIdentity.FromId(chatId),
                MalwareDetails = malwareDetails,
                Executor = Actor.FileScanner,
                Reason = malwareDetails
            });

        // Assert - Report was created with isAutomated=true
        await _mockReportService.Received(1).CreateReportAsync(
            Arg.Is<Report>(r => r.MessageId == (int)messageId && r.Chat.Id == chatId),
            null,
            true,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleMalwareViolationAsync_NotifiesAdmins()
    {
        // Arrange
        const long messageId = 1001L;
        const long chatId = 2002L;
        const long userId = 3003L;
        const string malwareDetails = "Virus.Win32.Agent detected";

        _mockMessageHandler.EnsureExistsAsync(messageId, Arg.Any<ChatIdentity>(), null, Arg.Any<CancellationToken>())
            .Returns(BackfillResult.AlreadyExists());
        _mockMessageHandler.DeleteAsync(Arg.Any<ChatIdentity>(), messageId, Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(DeleteResult.Succeeded(messageDeleted: true));

        // Act
        await _orchestrator.HandleMalwareViolationAsync(
            new MalwareViolationIntent
            {
                User = UserIdentity.FromId(userId),
                MessageId = messageId,
                Chat = ChatIdentity.FromId(chatId),
                MalwareDetails = malwareDetails,
                Executor = Actor.FileScanner,
                Reason = malwareDetails
            });

        // Assert - Admin notification was sent
        await _mockNotificationService.Received(1).SendSystemNotificationAsync(
            NotificationEventType.MalwareDetected,
            Arg.Any<string>(),
            Arg.Is<string>(msg => msg.Contains(malwareDetails)),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleMalwareViolationAsync_DoesNotBan()
    {
        // Arrange
        const long messageId = 1001L;
        const long chatId = 2002L;
        const long userId = 3003L;
        const string malwareDetails = "Trojan detected";

        _mockMessageHandler.EnsureExistsAsync(messageId, Arg.Any<ChatIdentity>(), null, Arg.Any<CancellationToken>())
            .Returns(BackfillResult.AlreadyExists());
        _mockMessageHandler.DeleteAsync(Arg.Any<ChatIdentity>(), messageId, Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(DeleteResult.Succeeded(messageDeleted: true));

        // Act
        await _orchestrator.HandleMalwareViolationAsync(
            new MalwareViolationIntent
            {
                User = UserIdentity.FromId(userId),
                MessageId = messageId,
                Chat = ChatIdentity.FromId(chatId),
                MalwareDetails = malwareDetails,
                Executor = Actor.FileScanner,
                Reason = malwareDetails
            });

        // Assert - BanAsync was NOT called (malware upload may be accidental)
        await _mockBanHandler.DidNotReceive().BanAsync(
            Arg.Any<UserIdentity>(),
            Arg.Any<Actor>(),
            Arg.Any<string>(),
            Arg.Any<long?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleMalwareViolationAsync_AuditsDelete()
    {
        // Arrange
        const long messageId = 1001L;
        const long chatId = 2002L;
        const long userId = 3003L;
        const string malwareDetails = "Malware detected";

        _mockMessageHandler.EnsureExistsAsync(messageId, Arg.Any<ChatIdentity>(), null, Arg.Any<CancellationToken>())
            .Returns(BackfillResult.AlreadyExists());
        _mockMessageHandler.DeleteAsync(Arg.Any<ChatIdentity>(), messageId, Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(DeleteResult.Succeeded(messageDeleted: true));

        // Act
        await _orchestrator.HandleMalwareViolationAsync(
            new MalwareViolationIntent
            {
                User = UserIdentity.FromId(userId),
                MessageId = messageId,
                Chat = ChatIdentity.FromId(chatId),
                MalwareDetails = malwareDetails,
                Executor = Actor.FileScanner,
                Reason = malwareDetails
            });

        // Assert - Audit log was created for deletion
        await _mockAuditHandler.Received(1).LogDeleteAsync(
            messageId,
            Arg.Any<ChatIdentity>(),
            Arg.Any<UserIdentity>(),
            Actor.FileScanner,
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region HandleCriticalViolationAsync Tests

    [Test]
    public async Task HandleCriticalViolationAsync_DeletesMessage()
    {
        // Arrange
        const long messageId = 1001L;
        const long chatId = 2002L;
        const long userId = 3003L;
        var violations = new List<string> { "Blocked URL: malware.com" };

        _mockMessageHandler.DeleteAsync(Arg.Any<ChatIdentity>(), messageId, Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(DeleteResult.Succeeded(messageDeleted: true));
        _mockNotificationHandler.NotifyUserCriticalViolationAsync(Arg.Any<UserIdentity>(), violations, Arg.Any<CancellationToken>())
            .Returns(NotificationResult.Succeeded());

        // Act
        var result = await _orchestrator.HandleCriticalViolationAsync(
            new CriticalViolationIntent
            {
                User = UserIdentity.FromId(userId),
                MessageId = messageId,
                Chat = ChatIdentity.FromId(chatId),
                Violations = violations,
                Executor = Actor.AutoDetection,
                Reason = string.Join(", ", violations)
            });

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.MessageDeleted, Is.True);

        // Verify message was deleted with AutoDetection actor
        await _mockMessageHandler.Received(1).DeleteAsync(
            Arg.Any<ChatIdentity>(),
            messageId,
            Actor.AutoDetection,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleCriticalViolationAsync_NotifiesUser()
    {
        // Arrange
        const long messageId = 1001L;
        const long chatId = 2002L;
        const long userId = 3003L;
        var violations = new List<string> { "Blocked URL", "Suspicious file" };

        _mockMessageHandler.DeleteAsync(Arg.Any<ChatIdentity>(), messageId, Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(DeleteResult.Succeeded(messageDeleted: true));
        _mockNotificationHandler.NotifyUserCriticalViolationAsync(Arg.Any<UserIdentity>(), violations, Arg.Any<CancellationToken>())
            .Returns(NotificationResult.Succeeded());

        // Act
        await _orchestrator.HandleCriticalViolationAsync(
            new CriticalViolationIntent
            {
                User = UserIdentity.FromId(userId),
                MessageId = messageId,
                Chat = ChatIdentity.FromId(chatId),
                Violations = violations,
                Executor = Actor.AutoDetection,
                Reason = string.Join(", ", violations)
            });

        // Assert - User notification was sent with all violations
        await _mockNotificationHandler.Received(1).NotifyUserCriticalViolationAsync(
            Arg.Is<UserIdentity>(u => u.Id == userId),
            Arg.Is<List<string>>(v => v.Count == 2 && v.Contains("Blocked URL") && v.Contains("Suspicious file")),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleCriticalViolationAsync_DoesNotBan()
    {
        // Arrange
        const long messageId = 1001L;
        const long chatId = 2002L;
        const long userId = 3003L;
        var violations = new List<string> { "Blocked URL" };

        _mockMessageHandler.DeleteAsync(Arg.Any<ChatIdentity>(), messageId, Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(DeleteResult.Succeeded(messageDeleted: true));
        _mockNotificationHandler.NotifyUserCriticalViolationAsync(Arg.Any<UserIdentity>(), violations, Arg.Any<CancellationToken>())
            .Returns(NotificationResult.Succeeded());

        // Act
        await _orchestrator.HandleCriticalViolationAsync(
            new CriticalViolationIntent
            {
                User = UserIdentity.FromId(userId),
                MessageId = messageId,
                Chat = ChatIdentity.FromId(chatId),
                Violations = violations,
                Executor = Actor.AutoDetection,
                Reason = string.Join(", ", violations)
            });

        // Assert - BanAsync was NOT called (trusted users get a pass)
        await _mockBanHandler.DidNotReceive().BanAsync(
            Arg.Any<UserIdentity>(),
            Arg.Any<Actor>(),
            Arg.Any<string>(),
            Arg.Any<long?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleCriticalViolationAsync_DoesNotWarn()
    {
        // Arrange
        const long messageId = 1001L;
        const long chatId = 2002L;
        const long userId = 3003L;
        var violations = new List<string> { "Blocked URL" };

        _mockMessageHandler.DeleteAsync(Arg.Any<ChatIdentity>(), messageId, Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(DeleteResult.Succeeded(messageDeleted: true));
        _mockNotificationHandler.NotifyUserCriticalViolationAsync(Arg.Any<UserIdentity>(), violations, Arg.Any<CancellationToken>())
            .Returns(NotificationResult.Succeeded());

        // Act
        await _orchestrator.HandleCriticalViolationAsync(
            new CriticalViolationIntent
            {
                User = UserIdentity.FromId(userId),
                MessageId = messageId,
                Chat = ChatIdentity.FromId(chatId),
                Violations = violations,
                Executor = Actor.AutoDetection,
                Reason = string.Join(", ", violations)
            });

        // Assert - WarnAsync was NOT called (trusted users get a pass)
        await _mockWarnHandler.DidNotReceive().WarnAsync(
            Arg.Any<UserIdentity>(),
            Arg.Any<Actor>(),
            Arg.Any<string?>(),
            Arg.Any<long>(),
            Arg.Any<long?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleCriticalViolationAsync_AuditsDelete()
    {
        // Arrange
        const long messageId = 1001L;
        const long chatId = 2002L;
        const long userId = 3003L;
        var violations = new List<string> { "Blocked URL" };

        _mockMessageHandler.DeleteAsync(Arg.Any<ChatIdentity>(), messageId, Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(DeleteResult.Succeeded(messageDeleted: true));
        _mockNotificationHandler.NotifyUserCriticalViolationAsync(Arg.Any<UserIdentity>(), violations, Arg.Any<CancellationToken>())
            .Returns(NotificationResult.Succeeded());

        // Act
        await _orchestrator.HandleCriticalViolationAsync(
            new CriticalViolationIntent
            {
                User = UserIdentity.FromId(userId),
                MessageId = messageId,
                Chat = ChatIdentity.FromId(chatId),
                Violations = violations,
                Executor = Actor.AutoDetection,
                Reason = string.Join(", ", violations)
            });

        // Assert - Audit log was created for deletion
        await _mockAuditHandler.Received(1).LogDeleteAsync(
            messageId,
            Arg.Any<ChatIdentity>(),
            Arg.Any<UserIdentity>(),
            Actor.AutoDetection,
            Arg.Any<CancellationToken>());
    }

    #endregion
}
