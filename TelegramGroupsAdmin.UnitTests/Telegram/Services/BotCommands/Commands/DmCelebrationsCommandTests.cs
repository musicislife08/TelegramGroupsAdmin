using NSubstitute;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;
using TelegramGroupsAdmin.Telegram.Services.DmCelebrations;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.BotCommands.Commands;

[TestFixture]
public class DmCelebrationsCommandTests
{
    private const long ChatId = -100059667856554L;
    private const long UserId = 42L;

    private IBanCelebrationSubscriptionService _subscriptions = null!;
    private DmCelebrationsCommand _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _subscriptions = Substitute.For<IBanCelebrationSubscriptionService>();
        _sut = new DmCelebrationsCommand(_subscriptions);
    }

    private static Message GroupMessage() => new()
    {
        Id = 10,
        Chat = new Chat { Id = ChatId, Type = ChatType.Supergroup, Title = "Workshop Alumni" },
        From = new User { Id = UserId, FirstName = "Kim" }
    };

    [Test]
    public async Task Execute_OnWithDmsEnabled_ConfirmsSubscription()
    {
        _subscriptions.SubscribeAsync(Arg.Any<ChatIdentity>(), Arg.Any<UserIdentity>(), Arg.Any<CancellationToken>())
            .Returns(DmCelebrationSubscribeResult.Subscribed);

        var result = await _sut.ExecuteAsync(GroupMessage(), ["on"], PermissionLevel.Member);

        Assert.That(result.Message.Text, Does.Contain("Workshop Alumni"));
        await _subscriptions.Received(1).SubscribeAsync(
            Arg.Is<ChatIdentity>(c => c!.Id == ChatId), Arg.Is<UserIdentity>(u => u!.Id == UserId), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Execute_OnAwaitingStart_RepliesWithNothingBecauseThePromptIsTheReply()
    {
        _subscriptions.SubscribeAsync(Arg.Any<ChatIdentity>(), Arg.Any<UserIdentity>(), Arg.Any<CancellationToken>())
            .Returns(DmCelebrationSubscribeResult.AwaitingStart);

        var result = await _sut.ExecuteAsync(GroupMessage(), ["on"], PermissionLevel.Member);

        Assert.That(result.Message.Text, Is.Empty);
    }

    [Test]
    public async Task Execute_UppercaseOnWithTrailingWords_Subscribes()
    {
        _subscriptions.SubscribeAsync(Arg.Any<ChatIdentity>(), Arg.Any<UserIdentity>(), Arg.Any<CancellationToken>())
            .Returns(DmCelebrationSubscribeResult.Subscribed);

        await _sut.ExecuteAsync(GroupMessage(), ["ON", "please"], PermissionLevel.Member);

        await _subscriptions.ReceivedWithAnyArgs(1).SubscribeAsync(default!, default!);
    }

    [Test]
    public async Task Execute_Off_Unsubscribes()
    {
        var result = await _sut.ExecuteAsync(GroupMessage(), ["off"], PermissionLevel.Member);

        Assert.That(result.Message.Text, Does.Contain("Workshop Alumni"));
        await _subscriptions.Received(1).UnsubscribeAsync(
            Arg.Is<ChatIdentity>(c => c!.Id == ChatId), Arg.Is<UserIdentity>(u => u!.Id == UserId), Arg.Any<CancellationToken>());
    }

    [TestCase(true, "/dmcelebrations off")]
    [TestCase(false, "/dmcelebrations on")]
    public async Task Execute_NoArgument_ReportsStateWithUsageHint(bool subscribed, string hint)
    {
        _subscriptions.IsSubscribedAsync(ChatId, UserId, Arg.Any<CancellationToken>()).Returns(subscribed);

        var result = await _sut.ExecuteAsync(GroupMessage(), [], PermissionLevel.Member);

        Assert.That(result.Message.Text, Does.Contain(hint));
    }

    [Test]
    public async Task Execute_InPrivateChat_RefusesAndTouchesNothing()
    {
        var dm = GroupMessage();
        dm.Chat = new Chat { Id = UserId, Type = ChatType.Private };

        var result = await _sut.ExecuteAsync(dm, ["on"], PermissionLevel.Member);

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

        var result = await _sut.ExecuteAsync(anonymous, ["on"], PermissionLevel.Member);

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
