using NSubstitute;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Services.Notifications;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;

namespace TelegramGroupsAdmin.UnitTests.Services.Notifications;

[TestFixture]
public class NotificationDmDispatcherTests
{
    private static readonly UserIdentity Recipient = new(42L, "Kim", null, "kim");

    private IBotDmService _dm = null!;
    private ITelegramUserRepository _users = null!;
    private NotificationDmDispatcher _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _dm = Substitute.For<IBotDmService>();
        _users = Substitute.For<ITelegramUserRepository>();
        _sut = new NotificationDmDispatcher(_dm, _users);
    }

    [Test]
    public async Task DispatchAsync_AnimationPayload_SendsAnimationWithHeaderAndCaption()
    {
        var payload = NotificationPayloadBuilder.Create("Workshop Alumni")
            .WithText("Spammer got banned!")
            .WithAnimation("/data/media/ban-gifs/1.gif", "file-1")
            .Build();

        await _sut.DispatchAsync(Recipient, payload, keyboard: null, CancellationToken.None);

        await _dm.Received(1).SendDmWithAnimationEntitiesAsync(
            Recipient,
            Arg.Is<TelegramMessage>(m => m!.Text.StartsWith("Workshop Alumni") && m.Text.Contains("Spammer got banned!")),
            "file-1",
            "/data/media/ban-gifs/1.gif",
            Arg.Any<CancellationToken>());
    }

    [Test]
    public void DispatchAsync_AnimationWithKeyboard_Throws()
    {
        var payload = NotificationPayloadBuilder.Create("Workshop Alumni")
            .WithText("Spammer got banned!")
            .WithAnimation("/data/media/ban-gifs/1.gif", "file-1")
            .Build();
        var keyboard = new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData("OK", "ok"));

        Assert.Throws<InvalidOperationException>(() =>
            _sut.DispatchAsync(Recipient, payload, keyboard, CancellationToken.None));
    }

    [Test]
    public async Task DispatchAsync_TextOnlyPayload_SendsEntitiesDm()
    {
        var payload = NotificationPayloadBuilder.Create("Subject").WithText("body").Build();

        await _sut.DispatchAsync(Recipient, payload, keyboard: null, CancellationToken.None);

        await _dm.Received(1).SendDmWithEntitiesAsync(Recipient, "notification", Arg.Any<string>(),
            Arg.Any<IReadOnlyList<MessageEntity>>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DispatchAsync_PhotoPayload_SendsMediaDm()
    {
        var payload = NotificationPayloadBuilder.Create("Subject").WithPhoto("/p.jpg").Build();

        await _sut.DispatchAsync(Recipient, payload, keyboard: null, CancellationToken.None);

        await _dm.Received(1).SendDmWithMediaAndKeyboardEntitiesAsync(Recipient, "notification", Arg.Any<string>(),
            Arg.Any<IReadOnlyList<MessageEntity>>(), "/p.jpg", null, null, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DispatchAsync_KeyboardOnlyPayload_SendsMediaAndKeyboardDmWithKeyboard()
    {
        // Admin report path: text payload plus action buttons, no media.
        var payload = NotificationPayloadBuilder.Create("Report").WithText("body").Build();
        var keyboard = new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData("Ban", "ban"));

        await _sut.DispatchAsync(Recipient, payload, keyboard, CancellationToken.None);

        await _dm.Received(1).SendDmWithMediaAndKeyboardEntitiesAsync(Recipient, "notification", Arg.Any<string>(),
            Arg.Any<IReadOnlyList<MessageEntity>>(), Arg.Is<string?>(x => x == null), Arg.Is<string?>(x => x == null),
            Arg.Is<InlineKeyboardMarkup?>(k => ReferenceEquals(k, keyboard)), Arg.Any<CancellationToken>());
        await _dm.DidNotReceiveWithAnyArgs().SendDmWithEntitiesAsync(default!, default!, default!, default!);
    }

    [Test]
    public async Task DispatchAsync_VideoOnlyPayload_SendsMediaDm()
    {
        var payload = NotificationPayloadBuilder.Create("Subject").WithVideo("/v.mp4").Build();

        await _sut.DispatchAsync(Recipient, payload, keyboard: null, CancellationToken.None);

        await _dm.Received(1).SendDmWithMediaAndKeyboardEntitiesAsync(Recipient, "notification", Arg.Any<string>(),
            Arg.Any<IReadOnlyList<MessageEntity>>(), Arg.Is<string?>(x => x == null), "/v.mp4",
            Arg.Is<InlineKeyboardMarkup?>(k => k == null), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DispatchAsync_ByTelegramId_ResolvesIdentityFromRepository()
    {
        var payload = NotificationPayloadBuilder.Create("Subject").WithText("body").Build();

        await _sut.DispatchAsync(42L, payload, keyboard: null, CancellationToken.None);

        await _users.Received(1).GetByTelegramIdAsync(42L, Arg.Any<CancellationToken>());
    }
}
