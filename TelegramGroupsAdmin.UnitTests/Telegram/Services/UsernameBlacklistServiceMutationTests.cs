using NSubstitute;
using NUnit.Framework;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services;

[TestFixture]
public class UsernameBlacklistServiceMutationTests
{
    private IUsernameBlacklistRepository _repo = null!;
    private IAuditService _audit = null!;
    private UsernameBlacklistService _service = null!;

    private static readonly Actor TestActor = Actor.FromSystem(SystemActorIds.WebAdmin);

    [SetUp]
    public void SetUp()
    {
        _repo = Substitute.For<IUsernameBlacklistRepository>();
        _audit = Substitute.For<IAuditService>();
        _service = new UsernameBlacklistService(_repo, _audit);
    }

    [Test]
    public async Task AddEntryAsync_CallsRepoThenWritesAuditWithDedicatedEventType()
    {
        _repo.AddEntryAsync(Arg.Any<UsernameBlacklistEntry>(), Arg.Any<CancellationToken>())
            .Returns(42L);

        var actor = TestActor;
        var id = await _service.AddEntryAsync(
            "spam-pattern",
            BlacklistMatchType.Exact,
            notes: "test notes",
            actor: actor,
            ct: CancellationToken.None);

        Assert.That(id, Is.EqualTo(42L));

        await _repo.Received(1).AddEntryAsync(
            Arg.Is<UsernameBlacklistEntry>(e =>
                e.Pattern == "spam-pattern"
                && e.MatchType == BlacklistMatchType.Exact
                && e.Notes == "test notes"
                && e.Enabled == true),
            Arg.Any<CancellationToken>());

        await _audit.Received(1).LogEventAsync(
            AuditEventType.BlacklistEntryAdded,
            actor,
            actor,
            "spam-pattern",
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeleteEntryAsync_WhenRepoReturnsTrue_WritesAudit()
    {
        _repo.DeleteEntryAsync(7L, Arg.Any<CancellationToken>()).Returns(true);
        var actor = TestActor;

        var result = await _service.DeleteEntryAsync(7L, "test-pattern", actor, CancellationToken.None);

        Assert.That(result, Is.True);
        await _audit.Received(1).LogEventAsync(
            AuditEventType.BlacklistEntryRemoved,
            actor, actor, "test-pattern",
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeleteEntryAsync_WhenRepoReturnsFalse_DoesNotWriteAudit()
    {
        _repo.DeleteEntryAsync(7L, Arg.Any<CancellationToken>()).Returns(false);
        var actor = TestActor;

        var result = await _service.DeleteEntryAsync(7L, "test-pattern", actor, CancellationToken.None);

        Assert.That(result, Is.False);
        await _audit.DidNotReceive().LogEventAsync(
            Arg.Any<AuditEventType>(), Arg.Any<Actor>(), Arg.Any<Actor?>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SetEnabledAsync_True_WritesEnabledAuditEvent()
    {
        _repo.SetEnabledAsync(7L, true, Arg.Any<CancellationToken>()).Returns(true);
        var actor = TestActor;

        var result = await _service.SetEnabledAsync(7L, "test-pattern", enabled: true, actor, CancellationToken.None);

        Assert.That(result, Is.True);
        await _audit.Received(1).LogEventAsync(
            AuditEventType.BlacklistEntryEnabled,
            actor, actor, "test-pattern",
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SetEnabledAsync_False_WritesDisabledAuditEvent()
    {
        _repo.SetEnabledAsync(7L, false, Arg.Any<CancellationToken>()).Returns(true);
        var actor = TestActor;

        var result = await _service.SetEnabledAsync(7L, "test-pattern", enabled: false, actor, CancellationToken.None);

        Assert.That(result, Is.True);
        await _audit.Received(1).LogEventAsync(
            AuditEventType.BlacklistEntryDisabled,
            actor, actor, "test-pattern",
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SetEnabledAsync_WhenRepoReturnsFalse_DoesNotWriteAudit()
    {
        _repo.SetEnabledAsync(7L, true, Arg.Any<CancellationToken>()).Returns(false);
        var actor = TestActor;

        var result = await _service.SetEnabledAsync(7L, "test-pattern", enabled: true, actor, CancellationToken.None);

        Assert.That(result, Is.False);
        await _audit.DidNotReceive().LogEventAsync(
            Arg.Any<AuditEventType>(), Arg.Any<Actor>(), Arg.Any<Actor?>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateNotesAsync_WhenRepoReturnsTrue_WritesAudit()
    {
        _repo.UpdateNotesAsync(7L, "new notes", Arg.Any<CancellationToken>()).Returns(true);
        var actor = TestActor;

        var result = await _service.UpdateNotesAsync(7L, "test-pattern", "new notes", actor, CancellationToken.None);

        Assert.That(result, Is.True);
        await _audit.Received(1).LogEventAsync(
            AuditEventType.BlacklistEntryNotesChanged,
            actor, actor, "test-pattern",
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateNotesAsync_WhenRepoReturnsFalse_DoesNotWriteAudit()
    {
        _repo.UpdateNotesAsync(7L, "new notes", Arg.Any<CancellationToken>()).Returns(false);
        var actor = TestActor;

        var result = await _service.UpdateNotesAsync(7L, "test-pattern", "new notes", actor, CancellationToken.None);

        Assert.That(result, Is.False);
        await _audit.DidNotReceive().LogEventAsync(
            Arg.Any<AuditEventType>(), Arg.Any<Actor>(), Arg.Any<Actor?>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }
}
