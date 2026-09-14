using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NUnit.Framework;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Handlers;
using TelegramGroupsAdmin.Telegram.Services.Welcome;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.Moderation.Handlers;

/// <summary>
/// Unit tests for AuditHandler - audit trail recording for moderation actions.
///
/// Architecture:
/// - AuditHandler writes UserActionRecord rows to the user_actions table
/// - Reason strings are private const on AuditHandler (no magic strings at call sites)
///
/// Test Coverage:
/// - LogWelcomeBypassAsync: Verifies correct ActionType, IssuedBy, ChatId, and Reason
///   for each BypassDecision variant (ChatAdmin, WebAdmin, Trusted).
/// - LogKickAsync / LogRestorePermissionsAsync: Verifies the ChatIdentity parameter is
///   persisted to the audit row (regression guard for the fix-as-found where chat_id was
///   silently dropped on these paths).
///
/// Mocking Strategy:
/// - NSubstitute for IUserActionsRepository
/// - NullLogger to satisfy constructor without noise
/// </summary>
[TestFixture]
public class AuditHandlerTests
{
    private IUserActionsRepository _userActionsRepo = null!;
    private AuditHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _userActionsRepo = Substitute.For<IUserActionsRepository>();
        _handler = new AuditHandler(
            _userActionsRepo,
            NullLogger<AuditHandler>.Instance);
    }

    [Test]
    public async Task LogWelcomeBypassAsync_AdminDecision_PersistsChatTaggedReason()
    {
        const string suppliedReason = "Telegram chat admin (3 chats)";
        UserActionRecord? captured = null;
        _userActionsRepo.InsertAsync(Arg.Do<UserActionRecord>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(1L);

        await _handler.LogWelcomeBypassAsync(
            UserIdentity.FromId(100),
            ChatIdentity.FromId(-200),
            BypassDecision.Admin,
            suppliedReason,
            CancellationToken.None);

        Assert.That(captured, Is.Not.Null);
        Assert.That(captured!.ActionType, Is.EqualTo(UserActionType.WelcomeBypass));
        Assert.That(captured.IssuedBy.GetSystemIdentifier(), Is.EqualTo(SystemActorIds.WelcomeBypass));
        Assert.That(captured.ChatId, Is.EqualTo(-200),
            "Bypass audit rows record the chat where the join occurred.");
        Assert.That(captured.MessageId, Is.Null,
            "Bypass has no specific message context.");
        Assert.That(captured.Reason, Is.EqualTo("[Chat -200] Telegram chat admin (3 chats)"));
    }

    [Test]
    public async Task LogWelcomeBypassAsync_TrustedDecision_PersistsChatTaggedReason()
    {
        const string suppliedReason = "Trusted user";
        UserActionRecord? captured = null;
        _userActionsRepo.InsertAsync(Arg.Do<UserActionRecord>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(1L);

        await _handler.LogWelcomeBypassAsync(
            UserIdentity.FromId(100),
            ChatIdentity.FromId(-200),
            BypassDecision.Trusted,
            suppliedReason,
            CancellationToken.None);

        Assert.That(captured!.ActionType, Is.EqualTo(UserActionType.WelcomeBypass));
        Assert.That(captured.ChatId, Is.EqualTo(-200));
        Assert.That(captured.MessageId, Is.Null);
        Assert.That(captured.Reason, Is.EqualTo("[Chat -200] Trusted user"));
    }

    [Test]
    public async Task LogWelcomeBypassAsync_WebAdminReason_PersistsChatTagged()
    {
        const string suppliedReason = "Linked web admin (GlobalAdmin)";
        UserActionRecord? captured = null;
        _userActionsRepo.InsertAsync(Arg.Do<UserActionRecord>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(1L);

        await _handler.LogWelcomeBypassAsync(
            UserIdentity.FromId(100),
            ChatIdentity.FromId(-200),
            BypassDecision.Admin,
            suppliedReason,
            CancellationToken.None);

        Assert.That(captured!.Reason, Is.EqualTo("[Chat -200] Linked web admin (GlobalAdmin)"));
    }

    [Test]
    public async Task LogKickAsync_PersistsChatId()
    {
        UserActionRecord? captured = null;
        _userActionsRepo.InsertAsync(Arg.Do<UserActionRecord>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(1L);

        var executor = Actor.FromTelegramUser(999, "Admin");
        await _handler.LogKickAsync(
            UserIdentity.FromId(100),
            ChatIdentity.FromId(-200),
            executor,
            "test reason",
            CancellationToken.None);

        Assert.That(captured, Is.Not.Null);
        Assert.That(captured!.ActionType, Is.EqualTo(UserActionType.Kick));
        Assert.That(captured.ChatId, Is.EqualTo(-200),
            "Kick audit row records the chat where the kick happened.");
        Assert.That(captured.MessageId, Is.Null,
            "Kick is not scoped to a specific message.");
        Assert.That(captured.Reason, Is.EqualTo("[Chat -200] test reason"));
    }

    [Test]
    public async Task LogRestorePermissionsAsync_PersistsChatId()
    {
        UserActionRecord? captured = null;
        _userActionsRepo.InsertAsync(Arg.Do<UserActionRecord>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(1L);

        var executor = Actor.FromSystem("ExamFlow");
        await _handler.LogRestorePermissionsAsync(
            UserIdentity.FromId(100),
            ChatIdentity.FromId(-200),
            executor,
            "exam passed",
            CancellationToken.None);

        Assert.That(captured, Is.Not.Null);
        Assert.That(captured!.ActionType, Is.EqualTo(UserActionType.RestorePermissions));
        Assert.That(captured.ChatId, Is.EqualTo(-200),
            "RestorePermissions audit row records the chat where permissions were restored.");
        Assert.That(captured.MessageId, Is.Null,
            "RestorePermissions is not scoped to a specific message.");
        Assert.That(captured.Reason, Is.EqualTo("[Chat -200] exam passed"));
    }

    [Test]
    public async Task LogRestrictAsync_WithChat_PersistsChatId()
    {
        UserActionRecord? captured = null;
        _userActionsRepo.InsertAsync(Arg.Do<UserActionRecord>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(1L);

        await _handler.LogRestrictAsync(
            UserIdentity.FromId(100),
            ChatIdentity.FromId(-200),
            Actor.AutoDetection,
            reason: "test mute",
            CancellationToken.None);

        Assert.That(captured, Is.Not.Null);
        Assert.That(captured!.ActionType, Is.EqualTo(UserActionType.Mute));
        Assert.That(captured.ChatId, Is.EqualTo(-200), "Mute audit row records the chat where the mute was applied");
    }

    [Test]
    public async Task LogRestrictAsync_NullChat_LeavesChatIdNull()
    {
        UserActionRecord? captured = null;
        _userActionsRepo.InsertAsync(Arg.Do<UserActionRecord>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(1L);

        await _handler.LogRestrictAsync(
            UserIdentity.FromId(100),
            chat: null,
            Actor.AutoDetection,
            reason: "global mute",
            CancellationToken.None);

        Assert.That(captured, Is.Not.Null);
        Assert.That(captured!.ChatId, Is.Null, "Global mute (null chat) leaves chat_id null");
        Assert.That(captured.MessageId, Is.Null);
    }

    private static readonly ChatIdentity MainCommunity = new(-100026957614982L, "Main Community");

    [Test]
    public async Task LogKickAsync_TagsReasonWithChat()
    {
        UserActionRecord? captured = null;
        _userActionsRepo.InsertAsync(Arg.Do<UserActionRecord>(r => captured = r), Arg.Any<CancellationToken>()).Returns(1L);

        await _handler.LogKickAsync(UserIdentity.FromId(100), MainCommunity, Actor.WelcomeFlow, "Welcome timeout", CancellationToken.None);

        Assert.That(captured, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(captured!.ChatId, Is.EqualTo(MainCommunity.Id));
            Assert.That(captured.Reason, Is.EqualTo("[Main Community] Welcome timeout"));
        }
    }

    [Test]
    public async Task LogDeleteAsync_NoReason_StoresTagOnly()
    {
        UserActionRecord? captured = null;
        _userActionsRepo.InsertAsync(Arg.Do<UserActionRecord>(r => captured = r), Arg.Any<CancellationToken>()).Returns(1L);

        await _handler.LogDeleteAsync(4242, MainCommunity, UserIdentity.FromId(100), Actor.AutoDetection, CancellationToken.None);

        Assert.That(captured, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(captured!.MessageId, Is.EqualTo(4242));
            Assert.That(captured.Reason, Is.EqualTo("[Main Community]"));
        }
    }

    [Test]
    public async Task LogRestorePermissionsAsync_TagsReasonWithChat()
    {
        UserActionRecord? captured = null;
        _userActionsRepo.InsertAsync(Arg.Do<UserActionRecord>(r => captured = r), Arg.Any<CancellationToken>()).Returns(1L);

        await _handler.LogRestorePermissionsAsync(UserIdentity.FromId(100), MainCommunity, Actor.WelcomeFlow, "Completed welcome/rules flow", CancellationToken.None);

        Assert.That(captured?.Reason, Is.EqualTo("[Main Community] Completed welcome/rules flow"));
    }

    [Test]
    public async Task LogRestrictAsync_NullChat_LeavesReasonUntagged()
    {
        UserActionRecord? captured = null;
        _userActionsRepo.InsertAsync(Arg.Do<UserActionRecord>(r => captured = r), Arg.Any<CancellationToken>()).Returns(1L);

        await _handler.LogRestrictAsync(UserIdentity.FromId(100), null, Actor.WelcomeFlow, "Pending welcome verification", CancellationToken.None);

        Assert.That(captured?.Reason, Is.EqualTo("Pending welcome verification"));
    }

    [Test]
    public async Task LogBanAsync_GlobalAction_StoresReasonVerbatim()
    {
        UserActionRecord? captured = null;
        _userActionsRepo.InsertAsync(Arg.Do<UserActionRecord>(r => captured = r), Arg.Any<CancellationToken>()).Returns(1L);

        await _handler.LogBanAsync(UserIdentity.FromId(100), Actor.AutoDetection, "Auto-ban: High confidence spam", CancellationToken.None);

        Assert.That(captured?.Reason, Is.EqualTo("Auto-ban: High confidence spam"));
    }
}
