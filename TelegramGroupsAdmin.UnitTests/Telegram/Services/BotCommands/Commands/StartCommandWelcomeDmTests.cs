using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;
using TelegramGroupsAdmin.Telegram.Services.DmCelebrations;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.BotCommands.Commands;

/// <summary>
/// The /start welcome deep link sends the welcome text as a DM to the joiner. Direct messages
/// to a person are never masked, whatever the chat's "Mask flagged names" setting.
/// </summary>
[TestFixture]
public class StartCommandWelcomeDmTests
{
    private const long ChatId = -100059667856554L;
    private const long UserId = 42L;

    private static readonly UserIdentity FlaggedSender =
        UserIdentity.ForTest(UserId, "Bad", verdict: NameVerdict.Explicit);

    private IBotMessageService _messages = null!;
    private IBotChatService _chats = null!;
    private IConfigService _config = null!;
    private ServiceProvider _provider = null!;
    private StartCommand _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _messages = Substitute.For<IBotMessageService>();
        _chats = Substitute.For<IBotChatService>();
        _config = Substitute.For<IConfigService>();
        _config.GetEffectiveWelcomeAsync(ChatId, Arg.Any<CancellationToken>())
            .Returns(new WelcomeConfig { MainWelcomeMessage = "Welcome {username} to {chat_name}" });
        // Masking is on for every chat; the DM must ignore it.
        _config.GetNameMaskingAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(NameMasking.On);
        _chats.GetChatAsync(ChatId, Arg.Any<CancellationToken>())
            .Returns(new ChatFullInfo { Id = ChatId, Type = ChatType.Supergroup, Title = "Workshop Alumni" });

        _provider = new ServiceCollection().AddSingleton(_config).BuildServiceProvider();

        _sut = new StartCommand(
            NullLogger<StartCommand>.Instance,
            Substitute.For<IWelcomeResponsesRepository>(),
            Substitute.For<ITelegramUserRepository>(),
            Substitute.For<IPendingNotificationsRepository>(),
            _provider,
            _messages,
            _chats,
            Substitute.For<IBotDmService>(),
            Substitute.For<IBanCelebrationSubscriptionService>());
    }

    [TearDown]
    public void TearDown() => _provider.Dispose();

    [Test]
    public async Task WelcomeDeepLink_FlaggedSenderWithMaskingOn_DmShowsRealName()
    {
        var message = new Message
        {
            Id = 1,
            Chat = new Chat { Id = UserId, Type = ChatType.Private },
            From = new User { Id = UserId, FirstName = "Bad" }
        };

        await _sut.ExecuteAsync(message, [$"welcome_{ChatId}_{UserId}"], PermissionLevel.Member, FlaggedSender);

        await _messages.Received(1).SendAndSaveMessageAsync(
            chatId: UserId,
            message: Arg.Is<TelegramMessage>(m =>
                m!.Text.Contains("Welcome Bad") && !m.Text.Contains(NameRedaction.Explicit)),
            cancellationToken: Arg.Any<CancellationToken>());
    }
}
