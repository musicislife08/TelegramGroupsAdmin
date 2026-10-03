using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;
using TelegramGroupsAdmin.Telegram.Services.DmCelebrations;
using TelegramGroupsAdmin.Telegram.Services.Identity;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.BotCommands.Commands;

[TestFixture]
public class StartCommandDmCelebrationsTests
{
    private const long ChatId = -100059667856554L;
    private const long UserId = 42L;

    private IBanCelebrationSubscriptionService _subscriptions = null!;
    private IUserIdentityService _identities = null!;
    private StartCommand _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _subscriptions = Substitute.For<IBanCelebrationSubscriptionService>();
        _identities = Substitute.For<IUserIdentityService>();
        _identities.ResolveAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(UserIdentity.ForTest(UserId, "Kim"));
        _sut = new StartCommand(
            NullLogger<StartCommand>.Instance,
            Substitute.For<IWelcomeResponsesRepository>(),
            Substitute.For<ITelegramUserRepository>(),
            Substitute.For<IPendingNotificationsRepository>(),
            Substitute.For<IServiceProvider>(),
            Substitute.For<IBotMessageService>(),
            Substitute.For<IBotChatService>(),
            Substitute.For<IBotDmService>(),
            _subscriptions,
            _identities);
    }

    private static Message PrivateStart() => new()
    {
        Id = 1,
        Chat = new Chat { Id = UserId, Type = ChatType.Private },
        From = new User { Id = UserId, FirstName = "Kim" }
    };

    [Test]
    public async Task StartCommand_DmCelebrationsPayload_Subscribed_ConfirmsWithChatName()
    {
        _subscriptions.ConfirmFromStartAsync(ChatId, Arg.Is<UserIdentity>(u => u!.Id == UserId), Arg.Any<CancellationToken>())
            .Returns(new ChatIdentity(ChatId, "Workshop Alumni"));

        var result = await _sut.ExecuteAsync(PrivateStart(), [$"dmcel_{ChatId}"], PermissionLevel.Member);

        Assert.That(result.Message.Text, Does.Contain("Workshop Alumni"));
    }

    [Test]
    public async Task StartCommand_DmCelebrationsPayload_NotSubscribed_RepliesNeutrally()
    {
        var result = await _sut.ExecuteAsync(PrivateStart(), [$"dmcel_{ChatId}"], PermissionLevel.Member);

        Assert.That(result.Message.Text, Does.Contain("/dmcelebrations on"));
    }

    [Test]
    public async Task StartCommand_MalformedDmCelebrationsPayload_FallsThroughToDefaultWelcome()
    {
        var result = await _sut.ExecuteAsync(PrivateStart(), ["dmcel_abc"], PermissionLevel.Member);

        Assert.That(result.Message.Text, Does.Contain("Welcome to TelegramGroupsAdmin Bot"));
        await _subscriptions.DidNotReceiveWithAnyArgs().ConfirmFromStartAsync(default, default!);
    }
}
