using Microsoft.Extensions.Logging.Abstractions;
using TelegramGroupsAdmin.Telegram.Services.Identity;
using NSubstitute;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Services;
using TelegramGroupsAdmin.Services.Notifications;
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;

namespace TelegramGroupsAdmin.UnitTests.Services;

[TestFixture]
public class UserNotificationServiceTests
{
    private BanCelebrationFanoutQueue _queue = null!;
    private IBanCelebrationGifRepository _gifs = null!;
    private IBotDmService _dm = null!;
    private UserNotificationService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _queue = new BanCelebrationFanoutQueue(new PipelineMetrics(), NullLogger<BanCelebrationFanoutQueue>.Instance);
        _gifs = Substitute.For<IBanCelebrationGifRepository>();
        _dm = Substitute.For<IBotDmService>();
        _gifs.GetFullPath(Arg.Any<string>()).Returns(ci => "/data/media/" + ci.Arg<string>());
        var sender = new BanCelebrationDmSender(new NotificationDmDispatcher(_dm, Substitute.For<IUserIdentityService>()), _gifs,
            NullLogger<BanCelebrationDmSender>.Instance);
        _sut = new UserNotificationService(_queue, _gifs, sender);
    }

    [Test]
    public async Task EnqueueBanCelebrationAsync_WritesOneItemToTheQueue()
    {
        var chat = new ChatIdentity(-100L, "Workshop Alumni");

        await _sut.EnqueueBanCelebrationAsync(chat, "banned!", 9);

        Assert.That(_queue.Reader.TryRead(out var item), Is.True);
        Assert.That(item, Is.EqualTo(new BanCelebrationFanoutItem(chat, "banned!", 9)));
    }

    [Test]
    public async Task SendBanCelebrationToBannedUserAsync_SendsAnimationAndReturnsDelivery()
    {
        _gifs.GetByIdAsync(9, Arg.Any<CancellationToken>())
            .Returns(new BanCelebrationGif { Id = 9, FilePath = "ban-gifs/9.gif", FileId = "cached" });
        _dm.SendDmWithAnimationEntitiesAsync(Arg.Any<UserIdentity>(), Arg.Any<TelegramMessage>(), "cached", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new DmDeliveryResult { DmSent = true, AnimationFileId = "cached" });

        var sent = await _sut.SendBanCelebrationToBannedUserAsync(
            new ChatIdentity(-100L, "Workshop Alumni"), new UserIdentity(7L, "Bad", null, null), "You got banned!", 9);

        Assert.That(sent, Is.True);
    }

    [Test]
    public async Task SendBanCelebrationToBannedUserAsync_DeliveryFails_ReturnsFalse()
    {
        _gifs.GetByIdAsync(9, Arg.Any<CancellationToken>())
            .Returns(new BanCelebrationGif { Id = 9, FilePath = "ban-gifs/9.gif", FileId = "cached" });
        _dm.SendDmWithAnimationEntitiesAsync(Arg.Any<UserIdentity>(), Arg.Any<TelegramMessage>(), "cached", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new DmDeliveryResult { DmSent = false, Failed = true });

        var sent = await _sut.SendBanCelebrationToBannedUserAsync(
            new ChatIdentity(-100L, "Workshop Alumni"), new UserIdentity(7L, "Bad", null, null), "You got banned!", 9);

        Assert.That(sent, Is.False);
    }

    [Test]
    public async Task SendBanCelebrationToBannedUserAsync_UnknownGif_ReturnsFalse()
    {
        var sent = await _sut.SendBanCelebrationToBannedUserAsync(
            new ChatIdentity(-100L, "Workshop Alumni"), new UserIdentity(7L, "Bad", null, null), "You got banned!", 9);

        Assert.That(sent, Is.False);
    }
}
