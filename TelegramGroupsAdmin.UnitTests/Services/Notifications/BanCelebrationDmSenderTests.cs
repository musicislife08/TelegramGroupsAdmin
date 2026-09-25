using NSubstitute;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Services.Notifications;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;

namespace TelegramGroupsAdmin.UnitTests.Services.Notifications;

[TestFixture]
public class BanCelebrationDmSenderTests
{
    private static readonly UserIdentity Recipient = new(42L, "Kim", null, "kim");

    private IBotDmService _dm = null!;
    private IBanCelebrationGifRepository _gifs = null!;
    private BanCelebrationDmSender _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _dm = Substitute.For<IBotDmService>();
        _gifs = Substitute.For<IBanCelebrationGifRepository>();
        _gifs.GetFullPath(Arg.Any<string>()).Returns(ci => "/data/media/" + ci.Arg<string>());
        _sut = new BanCelebrationDmSender(new NotificationDmDispatcher(_dm, Substitute.For<ITelegramUserRepository>()), _gifs);
    }

    private void DmReturns(DmDeliveryResult result) =>
        _dm.SendDmWithAnimationEntitiesAsync(Arg.Any<UserIdentity>(), Arg.Any<TelegramMessage>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(result);

    [Test]
    public async Task SendAsync_FirstUpload_CachesReturnedFileIdInRepositoryAndOnGif()
    {
        var gif = new BanCelebrationGif { Id = 3, FilePath = "ban-gifs/3.gif", FileId = null };
        DmReturns(new DmDeliveryResult { DmSent = true, AnimationFileId = "new-id" });

        await _sut.SendAsync(Recipient, new ChatIdentity(-100L, "Workshop Alumni"), "banned!", gif, CancellationToken.None);

        await _gifs.Received(1).UpdateFileIdAsync(3, "new-id", Arg.Any<CancellationToken>());
        Assert.That(gif.FileId, Is.EqualTo("new-id"));
        await _dm.Received(1).SendDmWithAnimationEntitiesAsync(Recipient, Arg.Any<TelegramMessage>(),
            null, "/data/media/ban-gifs/3.gif", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendAsync_SameFileIdReturned_DoesNotRewriteCache()
    {
        var gif = new BanCelebrationGif { Id = 3, FilePath = "ban-gifs/3.gif", FileId = "same" };
        DmReturns(new DmDeliveryResult { DmSent = true, AnimationFileId = "same" });

        await _sut.SendAsync(Recipient, new ChatIdentity(-100L, "Workshop Alumni"), "banned!", gif, CancellationToken.None);

        await _gifs.DidNotReceiveWithAnyArgs().UpdateFileIdAsync(default, default!);
    }

    [Test]
    public async Task SendAsync_ChatWithoutName_UsesChatIdAsHeader()
    {
        var gif = new BanCelebrationGif { Id = 3, FilePath = "ban-gifs/3.gif", FileId = "same" };
        DmReturns(new DmDeliveryResult { DmSent = true, AnimationFileId = "same" });

        await _sut.SendAsync(Recipient, ChatIdentity.FromId(-100123L), "banned!", gif, CancellationToken.None);

        await _dm.Received(1).SendDmWithAnimationEntitiesAsync(Recipient,
            Arg.Is<TelegramMessage>(m => m!.Text.StartsWith("-100123")),
            "same", Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }
}
