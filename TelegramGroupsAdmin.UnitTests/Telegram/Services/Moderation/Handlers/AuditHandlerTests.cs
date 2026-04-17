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
    public async Task LogWelcomeBypassAsync_ChatAdmin_WritesExpectedRow()
    {
        UserActionRecord? captured = null;
        _userActionsRepo.InsertAsync(Arg.Do<UserActionRecord>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(1L);

        await _handler.LogWelcomeBypassAsync(
            UserIdentity.FromId(100),
            ChatIdentity.FromId(-200),
            BypassDecision.ChatAdmin,
            CancellationToken.None);

        Assert.That(captured, Is.Not.Null);
        Assert.That(captured!.ActionType, Is.EqualTo(UserActionType.WelcomeBypass));
        Assert.That(captured.IssuedBy.GetSystemIdentifier(), Is.EqualTo(SystemActorIds.WelcomeBypass));
        Assert.That(captured.ChatId, Is.EqualTo(-200));
        Assert.That(captured.Reason, Is.EqualTo("Telegram chat admin/creator"));
    }

    [Test]
    public async Task LogWelcomeBypassAsync_WebAdmin_WritesExpectedRow()
    {
        UserActionRecord? captured = null;
        _userActionsRepo.InsertAsync(Arg.Do<UserActionRecord>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(1L);

        await _handler.LogWelcomeBypassAsync(
            UserIdentity.FromId(100),
            ChatIdentity.FromId(-200),
            BypassDecision.WebAdmin,
            CancellationToken.None);

        Assert.That(captured!.Reason, Is.EqualTo("Linked web admin (GlobalAdmin/Owner)"));
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
}
