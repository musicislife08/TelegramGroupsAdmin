using Microsoft.Extensions.Logging.Abstractions;
using TelegramGroupsAdmin.Telegram.Services.Identity;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Services.Notifications;
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.DmCelebrations;

namespace TelegramGroupsAdmin.UnitTests.Services.Notifications;

[TestFixture]
public class BanCelebrationFanoutProcessorTests
{
    private const long ChatId = -100059667856554L;
    private static readonly ChatIdentity Chat = new(ChatId, "Workshop Alumni");
    private static readonly UserIdentity A = new(1L, "A", null, "a");
    private static readonly UserIdentity B = new(2L, "B", null, "b");
    private static readonly UserIdentity C = new(3L, "C", null, "c");

    private IBanCelebrationSubscriberRepository _subscribers = null!;
    private IBanCelebrationGifRepository _gifs = null!;
    private IBanCelebrationSubscriptionService _subscriptions = null!;
    private IBotDmService _dm = null!;
    private IUserIdentityService _identities = null!;
    private BanCelebrationFanoutProcessor _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _subscribers = Substitute.For<IBanCelebrationSubscriberRepository>();
        _gifs = Substitute.For<IBanCelebrationGifRepository>();
        _subscriptions = Substitute.For<IBanCelebrationSubscriptionService>();
        _dm = Substitute.For<IBotDmService>();
        _identities = Substitute.For<IUserIdentityService>();
        _gifs.GetFullPath(Arg.Any<string>()).Returns(ci => "/data/media/" + ci.Arg<string>());
        _gifs.GetByIdAsync(9, Arg.Any<CancellationToken>())
            .Returns(new BanCelebrationGif { Id = 9, FilePath = "ban-gifs/9.gif", FileId = null });

        var sender = new BanCelebrationDmSender(new NotificationDmDispatcher(_dm, Substitute.For<IUserIdentityService>()), _gifs,
            NullLogger<BanCelebrationDmSender>.Instance);
        _sut = new BanCelebrationFanoutProcessor(_subscribers, _identities, _gifs, _subscriptions, sender,
            new PipelineMetrics(), NullLogger<BanCelebrationFanoutProcessor>.Instance);
    }

    private static BanCelebrationFanoutItem Item => new(Chat, "Spammer got banned!", 9);

    private void Subscribers(params UserIdentity[] users)
    {
        var ids = users.Select(u => u.Id).ToList();
        _subscribers.GetDeliverableSubscriberIdsAsync(ChatId, Arg.Any<CancellationToken>()).Returns(ids);
        _identities.ResolveManyAsync(Arg.Is<IReadOnlyCollection<long>>(r => r!.SequenceEqual(ids)), Arg.Any<CancellationToken>())
            .Returns(users.ToList());
    }

    [Test]
    public async Task ProcessAsync_SendsToSubscriberIdentitiesResolvedById()
    {
        // The repository yields ids only; the recipient identity comes from the identity service.
        var current = UserIdentity.ForTest(1L, "Current");
        Subscribers(current);
        DmFor(current, new DmDeliveryResult { DmSent = true });

        await _sut.ProcessAsync(Item, CancellationToken.None);

        await _identities.Received(1).ResolveManyAsync(
            Arg.Is<IReadOnlyCollection<long>>(r => r!.SequenceEqual(new[] { 1L })), Arg.Any<CancellationToken>());
        await _dm.Received(1).SendDmWithAnimationEntitiesAsync(current, Arg.Any<TelegramMessage>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    private void DmFor(UserIdentity user, DmDeliveryResult result) =>
        _dm.SendDmWithAnimationEntitiesAsync(user, Arg.Any<TelegramMessage>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(result);

    [Test]
    public async Task ProcessAsync_FirstSendUploads_LaterSendsReuseReturnedFileId()
    {
        Subscribers(A, B, C);
        DmFor(A, new DmDeliveryResult { DmSent = true, AnimationFileId = "fresh" });
        DmFor(B, new DmDeliveryResult { DmSent = true, AnimationFileId = "fresh" });
        DmFor(C, new DmDeliveryResult { DmSent = true, AnimationFileId = "fresh" });

        await _sut.ProcessAsync(Item, CancellationToken.None);

        await _dm.Received(1).SendDmWithAnimationEntitiesAsync(A, Arg.Any<TelegramMessage>(), Arg.Is<string?>(x => x == null), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await _dm.Received(1).SendDmWithAnimationEntitiesAsync(B, Arg.Any<TelegramMessage>(), "fresh", Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await _dm.Received(1).SendDmWithAnimationEntitiesAsync(C, Arg.Any<TelegramMessage>(), "fresh", Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await _gifs.Received(1).UpdateFileIdAsync(9, "fresh", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessAsync_BlockedSubscriber_RemovesTheirSubscriptionsAndContinues()
    {
        Subscribers(A, B);
        DmFor(A, new DmDeliveryResult { DmSent = false, Failed = true, Blocked = true });
        DmFor(B, new DmDeliveryResult { DmSent = true, AnimationFileId = "fresh" });

        await _sut.ProcessAsync(Item, CancellationToken.None);

        await _subscriptions.Received(1).RemoveAllForUserAsync(A, SubscriptionRemovalReason.Blocked, Arg.Any<CancellationToken>());
        await _dm.Received(1).SendDmWithAnimationEntitiesAsync(B, Arg.Any<TelegramMessage>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessAsync_OneSendThrows_ContinuesWithTheRest()
    {
        Subscribers(A, B);
        _dm.SendDmWithAnimationEntitiesAsync(A, Arg.Any<TelegramMessage>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("boom"));
        DmFor(B, new DmDeliveryResult { DmSent = true, AnimationFileId = "fresh" });

        await _sut.ProcessAsync(Item, CancellationToken.None);

        await _dm.Received(1).SendDmWithAnimationEntitiesAsync(B, Arg.Any<TelegramMessage>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessAsync_NonShutdownCancellationFromOneSend_ContinuesWithTheRest()
    {
        Subscribers(A, B);
        _dm.SendDmWithAnimationEntitiesAsync(A, Arg.Any<TelegramMessage>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());
        DmFor(B, new DmDeliveryResult { DmSent = true, AnimationFileId = "fresh" });

        await _sut.ProcessAsync(Item, CancellationToken.None);

        await _dm.Received(1).SendDmWithAnimationEntitiesAsync(B, Arg.Any<TelegramMessage>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessAsync_CancelledTokenDuringSend_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        Subscribers(A, B);
        _dm.SendDmWithAnimationEntitiesAsync(A, Arg.Any<TelegramMessage>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        Assert.ThrowsAsync<OperationCanceledException>(() => _sut.ProcessAsync(Item, cts.Token));

        await _dm.DidNotReceive().SendDmWithAnimationEntitiesAsync(B, Arg.Any<TelegramMessage>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessAsync_NonCancellationExceptionWhileTokenCancelled_IsCountedFailedNotThrown()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        Subscribers(A);
        _dm.SendDmWithAnimationEntitiesAsync(A, Arg.Any<TelegramMessage>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("connection pool disposed"));

        Assert.DoesNotThrowAsync(() => _sut.ProcessAsync(Item, cts.Token));
    }

    [Test]
    public async Task ProcessAsync_NonBlockedFailure_CountsFailedAndKeepsSubscriptions()
    {
        Subscribers(A, B);
        DmFor(A, new DmDeliveryResult { DmSent = false, Failed = true, Blocked = false });
        DmFor(B, new DmDeliveryResult { DmSent = true, AnimationFileId = "fresh" });

        await _sut.ProcessAsync(Item, CancellationToken.None);

        await _subscriptions.DidNotReceiveWithAnyArgs().RemoveAllForUserAsync(default!, default);
        await _dm.Received(1).SendDmWithAnimationEntitiesAsync(B, Arg.Any<TelegramMessage>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessAsync_NoDeliverableSubscribers_DoesNotLoadGif()
    {
        Subscribers();

        await _sut.ProcessAsync(Item, CancellationToken.None);

        await _gifs.DidNotReceiveWithAnyArgs().GetByIdAsync(default);
    }

    [Test]
    public async Task ProcessAsync_GifDeletedSinceEnqueue_SendsNothing()
    {
        Subscribers(A);
        _gifs.GetByIdAsync(9, Arg.Any<CancellationToken>()).Returns((BanCelebrationGif?)null);

        await _sut.ProcessAsync(Item, CancellationToken.None);

        await _dm.DidNotReceiveWithAnyArgs().SendDmWithAnimationEntitiesAsync(default!, default!, default, default);
    }
}
