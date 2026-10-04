using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;
using TelegramGroupsAdmin.Telegram.Services.Identity;
using TelegramGroupsAdmin.Telegram.Services.Moderation;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.BotCommands.Commands;

[TestFixture]
public class WarnCommandTests
{
    private const long TestChatId = -100999888L;
    private const long AdminId = 1001L;

    private static readonly UserIdentity Sender = UserIdentity.ForTest(AdminId, "Alex", username: "alex_admin");

    private IUserIdentityService _identities = null!;
    private IConfigService _config = null!;
    private IBotModerationService _moderation = null!;
    private IUserMessagingService _messaging = null!;
    private ServiceProvider _provider = null!;
    private WarnCommand _command = null!;

    [SetUp]
    public void SetUp()
    {
        _identities = Substitute.For<IUserIdentityService>();
        _config = Substitute.For<IConfigService>();
        _moderation = Substitute.For<IBotModerationService>();
        _messaging = Substitute.For<IUserMessagingService>();

        _moderation.WarnUserAsync(Arg.Any<WarnIntent>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true, WarningCount = 1 });
        _messaging.SendToUserAsync(Arg.Any<long>(), Arg.Any<Chat>(), Arg.Any<TelegramMessage>(),
                Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(ci => new MessageSendResult(ci.ArgAt<long>(0), true, MessageDeliveryMethod.PrivateDm));

        var services = new ServiceCollection();
        services.AddScoped(_ => Substitute.For<IChatAdminsRepository>());
        _provider = services.BuildServiceProvider();

        _command = new WarnCommand(
            NullLogger<WarnCommand>.Instance, _provider, _moderation, _messaging, _identities, _config);
    }

    [TearDown]
    public void TearDown() => _provider.Dispose();

    /// <summary>An admin's /warn replying to a message from <paramref name="targetId"/>, whose message carries stale names.</summary>
    private static Message WarnReplyTo(long targetId) => new()
    {
        Id = 50,
        From = new User { Id = AdminId, FirstName = "Alex" },
        Chat = new Chat { Id = TestChatId, Type = ChatType.Supergroup, Title = "Workshop" },
        Text = "/warn spam",
        ReplyToMessage = new Message
        {
            Id = 49,
            From = new User { Id = targetId, FirstName = "Stale", Username = "stale_name" },
            Chat = new Chat { Id = TestChatId, Type = ChatType.Supergroup, Title = "Workshop" },
            Text = "buy now"
        }
    };

    [Test]
    public async Task Warn_confirmation_mentions_target_with_masking()
    {
        var target = UserIdentity.ForTest(7, "Bad", verdict: NameVerdict.Explicit);
        _identities.ResolveAsync(7, Arg.Any<CancellationToken>()).Returns(target);
        _config.GetNameMaskingAsync(TestChatId, Arg.Any<CancellationToken>()).Returns(NameMasking.On);

        var result = await _command.ExecuteAsync(WarnReplyTo(7), ["spam"], PermissionLevel.Admin, Sender);

        Assert.That(result.Message.Text, Does.StartWith("⚠️ Warning issued to [name removed: explicit]"));
        Assert.That(result.Message.Entities.Single(e => e.Type == MessageEntityType.TextMention).User!.Id, Is.EqualTo(7));
    }

    [Test]
    public async Task Warn_intent_carries_resolved_target_and_sender_as_executor()
    {
        var target = UserIdentity.ForTest(7, "Current");
        _identities.ResolveAsync(7, Arg.Any<CancellationToken>()).Returns(target);
        _config.GetNameMaskingAsync(TestChatId, Arg.Any<CancellationToken>()).Returns(NameMasking.Off);

        var result = await _command.ExecuteAsync(WarnReplyTo(7), ["spam"], PermissionLevel.Admin, Sender);

        Assert.That(result.Message.Text, Does.StartWith("⚠️ Warning issued to Current"));
        await _moderation.Received(1).WarnUserAsync(
            Arg.Is<WarnIntent>(i => i!.User == target
                                    && i.Executor.TelegramUserId == AdminId
                                    && i.Executor.DisplayName == Sender.DisplayName
                                    && i.Reason == "spam"),
            Arg.Any<CancellationToken>());
    }
}
