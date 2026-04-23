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
    public async Task LogWelcomeBypassAsync_Admin_WritesExpectedRow()
    {
        UserActionRecord? captured = null;
        _userActionsRepo.InsertAsync(Arg.Do<UserActionRecord>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(1L);

        await _handler.LogWelcomeBypassAsync(
            UserIdentity.FromId(100),
            ChatIdentity.FromId(-200),
            BypassDecision.Admin,
            CancellationToken.None);

        Assert.That(captured, Is.Not.Null);
        Assert.That(captured!.ActionType, Is.EqualTo(UserActionType.WelcomeBypass));
        Assert.That(captured.IssuedBy.GetSystemIdentifier(), Is.EqualTo(SystemActorIds.WelcomeBypass));
        Assert.That(captured.ChatId, Is.EqualTo(-200),
            "Bypass audit rows record the chat where the join occurred.");
        Assert.That(captured.MessageId, Is.Null,
            "Bypass has no specific message context.");
        Assert.That(captured.Reason, Is.EqualTo("Admin identified (Telegram chat admin or linked web admin)"));
    }


    [Test]
    public async Task LogWelcomeBypassAsync_Trusted_WritesExpectedRow()
    {
        UserActionRecord? captured = null;
        _userActionsRepo.InsertAsync(Arg.Do<UserActionRecord>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(1L);

        await _handler.LogWelcomeBypassAsync(
            UserIdentity.FromId(100),
            ChatIdentity.FromId(-200),
            BypassDecision.Trusted,
            CancellationToken.None);

        Assert.That(captured!.Reason, Is.EqualTo("Trusted user, bypass enabled"));
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
        Assert.That(captured.Reason, Is.EqualTo("test reason"));
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
        Assert.That(captured.Reason, Is.EqualTo("exam passed"));
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
}
