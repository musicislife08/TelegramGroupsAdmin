using Microsoft.Extensions.Logging;
using NSubstitute;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Identity;
using TelegramGroupsAdmin.Telegram.Services.UserApi;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.Identity;

[TestFixture]
public class UserIdentityServiceTests
{
    private static readonly ChatIdentity Chat = new(-100, "Chat");
    private ITelegramUserRepository _users = null!;
    private IProfileScanGate _gate = null!;
    private UserIdentityService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _users = Substitute.For<ITelegramUserRepository>();
        _gate = Substitute.For<IProfileScanGate>();
        _sut = new UserIdentityService(_users, _gate, Substitute.For<ILogger<UserIdentityService>>());
    }

    private static TelegramUser Row(long id, string first, bool trusted = false, bool bot = false, bool banned = false)
    {
        var now = DateTimeOffset.UtcNow;
        return new TelegramUser(
            TelegramUserId: id,
            Username: null, FirstName: first, LastName: null,
            UserPhotoPath: null, PhotoHash: null, PhotoFileUniqueId: null,
            IsBot: bot, IsTrusted: trusted, IsBanned: banned,
            KickCount: 0, BotDmEnabled: false,
            FirstSeenAt: now, LastSeenAt: now, CreatedAt: now, UpdatedAt: now);
    }

    private static ObservedUser Observed(long id, string first, ObservationSource source = ObservationSource.BotUpdate) =>
        new(id, first, null, null, IsBot: false, source, DateTimeOffset.UtcNow);

    private void IdentityRow(long id, string first, NameVerdict verdict) =>
        _users.GetIdentitiesAsync(Arg.Is<IReadOnlyCollection<long>>(ids => ids!.Contains(id)), Arg.Any<CancellationToken>())
            .Returns([UserIdentity.ForTest(id, first, verdict: verdict)]);

    private void Renamed(TelegramUser row) =>
        _users.GetOrUpdateAsync(Arg.Any<ObservedUser>(), Arg.Any<ProfileChangeContext>(), Arg.Any<CancellationToken>())
            .Returns(new ObservedNamesResult(row, new PreviousNames("Old", null, null)));

    [Test]
    public async Task Resolve_ReturnsIdentityFromView()
    {
        IdentityRow(7, "Bad", NameVerdict.Explicit);

        var identity = await _sut.ResolveAsync(7);

        Assert.That(identity.Verdict, Is.EqualTo(NameVerdict.Explicit));
        Assert.That(identity.DisplayName, Is.EqualTo("Bad"));
    }

    [Test]
    public async Task Resolve_UnknownId_GivesIdOnlyUnscanned()
    {
        _users.GetIdentitiesAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>()).Returns([]);

        var identity = await _sut.ResolveAsync(7);

        Assert.That(identity.DisplayName, Is.EqualTo("User 7"));
        Assert.That(identity.Verdict, Is.EqualTo(NameVerdict.Unscanned));
    }

    [Test]
    public async Task ResolveMany_KeepsRequestedOrder_AndFillsUnknownIds()
    {
        _users.GetIdentitiesAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>())
            .Returns([UserIdentity.ForTest(2, "B"), UserIdentity.ForTest(1, "A")]);

        var result = await _sut.ResolveManyAsync([1, 2, 3]);

        Assert.That(result.Select(i => i.Id), Is.EqualTo(new long[] { 1, 2, 3 }));
        Assert.That(result[2].DisplayName, Is.EqualTo("User 3"));
        await _users.Received(1).GetIdentitiesAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Resolve_RepositoryThrows_GivesIdOnlyUnscanned()
    {
        _users.GetIdentitiesAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<UserIdentity>>(_ => throw new InvalidOperationException("db down"));

        var identity = await _sut.ResolveAsync(7);

        Assert.That(identity.Id, Is.EqualTo(7));
        Assert.That(identity.DisplayName, Is.EqualTo("User 7"));
        Assert.That(identity.Verdict, Is.EqualTo(NameVerdict.Unscanned));
    }

    [Test]
    public async Task ResolveMany_RepositoryThrows_GivesOneIdOnlyIdentityPerIdInOrder()
    {
        _users.GetIdentitiesAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<UserIdentity>>(_ => throw new InvalidOperationException("db down"));

        var result = await _sut.ResolveManyAsync([3, 1, 2]);

        Assert.That(result.Select(i => i.Id), Is.EqualTo(new long[] { 3, 1, 2 }));
        Assert.That(result.Select(i => i.DisplayName), Is.EqualTo(new[] { "User 3", "User 1", "User 2" }));
        Assert.That(result.All(i => i.Verdict == NameVerdict.Unscanned), Is.True);
    }

    [Test]
    public void Resolve_Cancelled_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _users.GetIdentitiesAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<UserIdentity>>(_ => throw new OperationCanceledException(cts.Token));

        Assert.CatchAsync<OperationCanceledException>(() => _sut.ResolveAsync(7, cts.Token));
    }

    [Test]
    public void ResolveMany_Cancelled_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _users.GetIdentitiesAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<UserIdentity>>(_ => throw new OperationCanceledException(cts.Token));

        Assert.CatchAsync<OperationCanceledException>(() => _sut.ResolveManyAsync([7, 8], cts.Token));
    }

    // ── Cancellation is never swallowed by ObserveAsync's fallbacks ──

    [Test]
    public void Observe_CancelledWhileRecording_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _users.GetOrUpdateAsync(Arg.Any<ObservedUser>(), Arg.Any<ProfileChangeContext>(), Arg.Any<CancellationToken>())
            .Returns<ObservedNamesResult>(_ => throw new OperationCanceledException(cts.Token));

        Assert.CatchAsync<OperationCanceledException>(() =>
            _sut.ObserveAsync(Observed(7, "Seen"), new ProfileChangeContext(Chat, 5), cts.Token));
    }

    [Test]
    public void Observe_CancelledWhileReadingIdentity_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        Renamed(Row(7, "New"));
        _users.GetIdentitiesAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<UserIdentity>>(_ =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            });

        Assert.CatchAsync<OperationCanceledException>(() =>
            _sut.ObserveAsync(Observed(7, "New"), new ProfileChangeContext(Chat, 5), cts.Token));
    }

    [Test]
    public void Observe_CancelledDuringRenameRescan_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        Renamed(Row(7, "New"));
        IdentityRow(7, "New", NameVerdict.Clean);
        _gate.ScanIfEligibleAsync(default!, default, default, default, default)
            .ReturnsForAnyArgs<ProfileScanResult?>(_ =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            });

        Assert.CatchAsync<OperationCanceledException>(() =>
            _sut.ObserveAsync(Observed(7, "New"), new ProfileChangeContext(Chat, 5), cts.Token));
    }

    [Test]
    public async Task Observe_BotUpdateRenameOfUntrustedUser_RescansThroughGate()
    {
        Renamed(Row(7, "New"));
        IdentityRow(7, "New", NameVerdict.Clean);

        await _sut.ObserveAsync(Observed(7, "New"), new ProfileChangeContext(Chat, 5));

        await _gate.Received(1).ScanIfEligibleAsync(
            Arg.Is<UserIdentity>(u => u!.Id == 7), Chat, ProfileScanTrigger.ProfileChange,
            Arg.Any<CancellationToken>(), forceRescan: true);
    }

    [Test]
    public async Task Observe_ChatMemberRename_RecordsOnly()
    {
        // Joins and admin refresh: muting comes first on a join, so nothing slow runs here; the
        // join scan picks the rename up from username_history.
        Renamed(Row(7, "New"));
        IdentityRow(7, "New", NameVerdict.Clean);

        var identity = await _sut.ObserveAsync(Observed(7, "New", ObservationSource.ChatMember), new ProfileChangeContext(Chat, null));

        Assert.That(identity.DisplayName, Is.EqualTo("New"));
        await _users.Received(1).GetOrUpdateAsync(
            Arg.Is<ObservedUser>(o => o!.Id == 7 && o.FirstName == "New"), Arg.Any<ProfileChangeContext>(), Arg.Any<CancellationToken>());
        await _gate.DidNotReceiveWithAnyArgs().ScanIfEligibleAsync(default!, default, default, default);
    }

    [Test]
    public async Task Observe_UserApiScanRename_RecordsOnly()
    {
        // The scan's own observation: rescanning would recurse.
        Renamed(Row(7, "New"));
        IdentityRow(7, "New", NameVerdict.Clean);

        await _sut.ObserveAsync(Observed(7, "New", ObservationSource.UserApiScan), new ProfileChangeContext(Chat, null));

        await _users.ReceivedWithAnyArgs(1).GetOrUpdateAsync(default!, default!, default);
        await _gate.DidNotReceiveWithAnyArgs().ScanIfEligibleAsync(default!, default, default, default);
    }

    [Test]
    public async Task Observe_RenameOfBannedUser_RecordsOnly()
    {
        Renamed(Row(7, "New", banned: true));
        IdentityRow(7, "New", NameVerdict.Clean);

        await _sut.ObserveAsync(Observed(7, "New"), new ProfileChangeContext(Chat, 5));

        await _users.ReceivedWithAnyArgs(1).GetOrUpdateAsync(default!, default!, default);
        await _gate.DidNotReceiveWithAnyArgs().ScanIfEligibleAsync(default!, default, default, default);
    }

    [Test]
    public async Task Observe_RenameOfTrustedUser_DoesNotScan()
    {
        Renamed(Row(7, "New", trusted: true));
        IdentityRow(7, "New", NameVerdict.Unscanned);

        await _sut.ObserveAsync(Observed(7, "New"), new ProfileChangeContext(Chat, 5));

        await _gate.DidNotReceiveWithAnyArgs().ScanIfEligibleAsync(default!, default, default, default);
    }

    [Test]
    public async Task Observe_NoRename_DoesNotScan()
    {
        _users.GetOrUpdateAsync(Arg.Any<ObservedUser>(), Arg.Any<ProfileChangeContext>(), Arg.Any<CancellationToken>())
            .Returns(new ObservedNamesResult(Row(7, "Same"), Renamed: null));
        IdentityRow(7, "Same", NameVerdict.Clean);

        await _sut.ObserveAsync(Observed(7, "Same"), new ProfileChangeContext(Chat, 5));

        await _gate.DidNotReceiveWithAnyArgs().ScanIfEligibleAsync(default!, default, default, default);
    }

    [Test]
    public async Task Observe_AfterRescan_ReturnsNewVerdict()
    {
        Renamed(Row(7, "New"));
        _users.GetIdentitiesAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>())
            .Returns([UserIdentity.ForTest(7, "New", verdict: NameVerdict.Clean)],
                     [UserIdentity.ForTest(7, "New", verdict: NameVerdict.Explicit)]);

        var identity = await _sut.ObserveAsync(Observed(7, "New"), new ProfileChangeContext(Chat, 5));

        Assert.That(identity.Verdict, Is.EqualTo(NameVerdict.Explicit));
    }

    [Test]
    public async Task Observe_RescanThrows_StillReturnsIdentity()
    {
        Renamed(Row(7, "New"));
        IdentityRow(7, "New", NameVerdict.Clean);
        _gate.ScanIfEligibleAsync(default!, default, default, default, default)
            .ReturnsForAnyArgs<ProfileScanResult?>(_ => throw new InvalidOperationException("scan failed"));

        var identity = await _sut.ObserveAsync(Observed(7, "New"), new ProfileChangeContext(Chat, 5));

        Assert.That(identity.Id, Is.EqualTo(7));
    }

    [Test]
    public async Task Observe_RepositoryThrows_ReturnsObservedNamesUnscanned()
    {
        _users.GetOrUpdateAsync(Arg.Any<ObservedUser>(), Arg.Any<ProfileChangeContext>(), Arg.Any<CancellationToken>())
            .Returns<ObservedNamesResult>(_ => throw new InvalidOperationException("db down"));

        var identity = await _sut.ObserveAsync(Observed(7, "Seen"), new ProfileChangeContext(null, null));

        Assert.That(identity.DisplayName, Is.EqualTo("Seen"));
        Assert.That(identity.Verdict, Is.EqualTo(NameVerdict.Unscanned));
    }

    [Test]
    public async Task Observe_IdentityReadThrows_ReturnsObservedNamesUnscanned_AndDoesNotScan()
    {
        Renamed(Row(7, "New"));
        _users.GetIdentitiesAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<UserIdentity>>(_ => throw new InvalidOperationException("db down"));

        var identity = await _sut.ObserveAsync(Observed(7, "Seen"), new ProfileChangeContext(Chat, 5));

        Assert.That(identity.DisplayName, Is.EqualTo("Seen"));
        Assert.That(identity.Verdict, Is.EqualTo(NameVerdict.Unscanned));
        await _gate.DidNotReceiveWithAnyArgs().ScanIfEligibleAsync(default!, default, default, default);
    }

    [Test]
    public async Task Observe_RenameOfBot_DoesNotScan()
    {
        Renamed(Row(7, "New", bot: true));
        IdentityRow(7, "New", NameVerdict.Unscanned);

        await _sut.ObserveAsync(Observed(7, "New"), new ProfileChangeContext(Chat, 5));

        await _gate.DidNotReceiveWithAnyArgs().ScanIfEligibleAsync(default!, default, default, default);
    }

    [Test]
    public async Task Observe_RenameOfSystemAccount_DoesNotScan()
    {
        const long id = TelegramGroupsAdmin.Core.TelegramConstants.ServiceAccountUserId;
        Renamed(Row(id, "New"));
        IdentityRow(id, "New", NameVerdict.Unscanned);

        await _sut.ObserveAsync(Observed(id, "New"), new ProfileChangeContext(Chat, 5));

        await _gate.DidNotReceiveWithAnyArgs().ScanIfEligibleAsync(default!, default, default, default);
    }
}
