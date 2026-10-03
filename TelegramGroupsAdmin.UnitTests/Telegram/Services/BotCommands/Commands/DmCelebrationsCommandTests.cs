using NSubstitute;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;
using TelegramGroupsAdmin.Telegram.Services.DmCelebrations;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.BotCommands.Commands;

[TestFixture]
public class DmCelebrationsCommandTests
{
    private const long ChatId = -100059667856554L;
    private const long UserId = 42L;

    // The pipeline's resolved sender; the message's own From carries stale names.
    private static readonly UserIdentity Sender = UserIdentity.ForTest(UserId, "Resolved");

    private IBanCelebrationSubscriptionService _subscriptions = null!;
    private IBotDmService _dm = null!;
    private DmCelebrationsCommand _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _subscriptions = Substitute.For<IBanCelebrationSubscriptionService>();
        _dm = Substitute.For<IBotDmService>();
        _sut = new DmCelebrationsCommand(_subscriptions, _dm);
    }

    /// <summary>Asserts the reply went to the user's DMs, falling back to a 30s group post only if the DM fails.</summary>
    private Task DmReceivedContaining(string text) =>
        _dm.Received(1).SendDmAsync(
            Arg.Is<UserIdentity>(u => u!.Id == UserId),
            Arg.Is<TelegramMessage>(m => m!.Text.Contains(text)),
            ChatId, 30, Arg.Any<CancellationToken>());

    private static Message GroupMessage() => new()
    {
        Id = 10,
        Chat = new Chat { Id = ChatId, Type = ChatType.Supergroup, Title = "Workshop Alumni" },
        From = new User { Id = UserId, FirstName = "Kim" }
    };

    [Test]
    public async Task Execute_OnSubscribed_RepliesWithNothingBecauseTheConfirmationIsADm()
    {
        _subscriptions.SubscribeAsync(Arg.Any<ChatIdentity>(), Arg.Any<UserIdentity>(), Arg.Any<CancellationToken>())
            .Returns(DmCelebrationSubscribeResult.Subscribed);

        var result = await _sut.ExecuteAsync(GroupMessage(), ["on"], PermissionLevel.Member, Sender);

        Assert.That(result.Message.Text, Is.Empty);
        await _subscriptions.Received(1).SubscribeAsync(
            Arg.Is<ChatIdentity>(c => c!.Id == ChatId), Arg.Is(Sender), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Execute_OnAwaitingStart_RepliesWithNothingBecauseThePromptIsTheReply()
    {
        _subscriptions.SubscribeAsync(Arg.Any<ChatIdentity>(), Arg.Any<UserIdentity>(), Arg.Any<CancellationToken>())
            .Returns(DmCelebrationSubscribeResult.AwaitingStart);

        var result = await _sut.ExecuteAsync(GroupMessage(), ["on"], PermissionLevel.Member, Sender);

        Assert.That(result.Message.Text, Is.Empty);
    }

    [Test]
    public async Task Execute_OnWhenNotAllowed_RepliesWithNothing()
    {
        _subscriptions.SubscribeAsync(Arg.Any<ChatIdentity>(), Arg.Any<UserIdentity>(), Arg.Any<CancellationToken>())
            .Returns(DmCelebrationSubscribeResult.NotAllowed);

        var result = await _sut.ExecuteAsync(GroupMessage(), ["on"], PermissionLevel.Member, Sender);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Message.Text, Is.Empty);
            Assert.That(result.DeleteCommandMessage, Is.True);
        }
    }

    [Test]
    public async Task Execute_UppercaseOnWithTrailingWords_Subscribes()
    {
        _subscriptions.SubscribeAsync(Arg.Any<ChatIdentity>(), Arg.Any<UserIdentity>(), Arg.Any<CancellationToken>())
            .Returns(DmCelebrationSubscribeResult.Subscribed);

        await _sut.ExecuteAsync(GroupMessage(), ["ON", "please"], PermissionLevel.Member, Sender);

        await _subscriptions.ReceivedWithAnyArgs(1).SubscribeAsync(default!, default!);
    }

    [Test]
    public async Task Execute_Off_UnsubscribesAndConfirmsByDm()
    {
        var result = await _sut.ExecuteAsync(GroupMessage(), ["off"], PermissionLevel.Member, Sender);

        Assert.That(result.Message.Text, Is.Empty);
        await DmReceivedContaining("won't get Workshop Alumni's ban celebrations");
        await _subscriptions.Received(1).UnsubscribeAsync(
            Arg.Is<ChatIdentity>(c => c!.Id == ChatId), Arg.Is<UserIdentity>(u => u!.Id == UserId), Arg.Any<CancellationToken>());
    }

    [TestCase(true, "/dmcelebrations off")]
    [TestCase(false, "/dmcelebrations on")]
    public async Task Execute_NoArgument_ReportsStateByDmWithUsageHint(bool subscribed, string hint)
    {
        _subscriptions.IsSubscribedAsync(ChatId, UserId, Arg.Any<CancellationToken>()).Returns(subscribed);

        var result = await _sut.ExecuteAsync(GroupMessage(), [], PermissionLevel.Member, Sender);

        Assert.That(result.Message.Text, Is.Empty);
        await DmReceivedContaining(hint);
    }

    [Test]
    public async Task Execute_InPrivateChat_RefusesAndTouchesNothing()
    {
        var dm = GroupMessage();
        dm.Chat = new Chat { Id = UserId, Type = ChatType.Private };

        var result = await _sut.ExecuteAsync(dm, ["on"], PermissionLevel.Member, Sender);

        Assert.That(result.Message.Text, Does.Contain("in the group"));
        await _subscriptions.DidNotReceiveWithAnyArgs().SubscribeAsync(default!, default!);
    }

    [Test]
    public async Task Execute_SentAsTheGroupOrAChannel_RefusesAndTouchesNothing()
    {
        // Anonymous admins arrive as GroupAnonymousBot with SenderChat set to the group.
        var anonymous = GroupMessage();
        anonymous.From = new User { Id = 1087968824, IsBot = true, FirstName = "Group", Username = "GroupAnonymousBot" };
        anonymous.SenderChat = new Chat { Id = ChatId, Type = ChatType.Supergroup, Title = "Workshop Alumni" };

        var result = await _sut.ExecuteAsync(anonymous, ["on"], PermissionLevel.Member, Sender);

        Assert.That(result.Message.Text, Does.Contain("your own account"));
        await _subscriptions.DidNotReceiveWithAnyArgs().SubscribeAsync(default!, default!);
    }

    [Test]
    public void Metadata_MatchesTheSpec()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_sut.Name, Is.EqualTo("dmcelebrations"));
            Assert.That(_sut.MinPermissionLevel, Is.EqualTo(PermissionLevel.Member));
            Assert.That(_sut.DeleteCommandMessage, Is.True);
            Assert.That(_sut.DeleteResponseAfterSeconds, Is.EqualTo(30));
        }
    }
}
