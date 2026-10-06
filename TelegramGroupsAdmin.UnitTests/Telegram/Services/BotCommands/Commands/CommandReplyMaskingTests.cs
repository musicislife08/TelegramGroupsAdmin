using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.BotCommands;
using TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;
using TelegramGroupsAdmin.Telegram.Services.Identity;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using TelegramGroupsAdmin.Telegram.Services.Notifications;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.BotCommands.Commands;

/// <summary>
/// Command replies are posted into the group chat. A reply that names a flagged user follows the
/// chat's "Mask flagged names" setting; a reply that names no user never reads the setting.
/// </summary>
[TestFixture]
public class CommandReplyMaskingTests
{
    private const long TestChatId = -100999888L;
    private const long AdminId = 1001L;
    private const long TargetId = 7L;

    private static readonly UserIdentity FlaggedSender =
        UserIdentity.ForTest(AdminId, "Rude", "Admin", verdict: NameVerdict.Explicit);
    private static readonly UserIdentity FlaggedTarget =
        UserIdentity.ForTest(TargetId, "Bad", "Name", verdict: NameVerdict.Explicit);

    private IUserIdentityService _identities = null!;
    private IConfigService _config = null!;
    private IBotModerationService _moderation = null!;
    private ITelegramUserRepository _users = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _identities = Substitute.For<IUserIdentityService>();
        _identities.ResolveAsync(TargetId, Arg.Any<CancellationToken>()).Returns(FlaggedTarget);
        _config = Substitute.For<IConfigService>();

        var success = new ModerationResult { Success = true, ChatsAffected = 1 };
        _moderation = Substitute.For<IBotModerationService>();
        _moderation.RestrictUserAsync(Arg.Any<RestrictIntent>(), Arg.Any<CancellationToken>()).Returns(success);
        _moderation.TempBanUserAsync(Arg.Any<TempBanIntent>(), Arg.Any<CancellationToken>()).Returns(success);
        _moderation.UnbanUserAsync(Arg.Any<UnbanIntent>(), Arg.Any<CancellationToken>()).Returns(success);

        _users = Substitute.For<ITelegramUserRepository>();
        var now = DateTimeOffset.UtcNow;
        _users.GetByTelegramIdAsync(AdminId, Arg.Any<CancellationToken>()).Returns(new TelegramUser(
            TelegramUserId: AdminId, Username: null, FirstName: "Rude", LastName: "Admin",
            UserPhotoPath: null, PhotoHash: null, PhotoFileUniqueId: null,
            IsBot: false, IsTrusted: false, IsBanned: false, KickCount: 0, BotDmEnabled: true,
            FirstSeenAt: now, LastSeenAt: now, CreatedAt: now, UpdatedAt: now));

        var services = new ServiceCollection();
        services.AddScoped(_ => Substitute.For<IChatAdminsRepository>());
        services.AddScoped(_ => _users);
        services.AddScoped(_ => Substitute.For<IBotMessageService>());
        foreach (var name in CommandNames.All)
        {
            var stub = Substitute.For<IBotCommand>();
            stub.Name.Returns(name);
            stub.Description.Returns($"{name} description");
            stub.MinPermissionLevel.Returns(PermissionLevel.Member);
            services.AddKeyedScoped(name, (_, _) => stub);
        }
        _provider = services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown() => _provider.Dispose();

    private static Message InGroup(string text, bool reply) => new()
    {
        Id = 50,
        From = new User { Id = AdminId, FirstName = "Rude", LastName = "Admin" },
        Chat = new Chat { Id = TestChatId, Type = ChatType.Supergroup, Title = "Workshop" },
        Text = text,
        ReplyToMessage = reply
            ? new Message
            {
                Id = 49,
                From = new User { Id = TargetId, FirstName = "Bad", LastName = "Name" },
                Chat = new Chat { Id = TestChatId, Type = ChatType.Supergroup, Title = "Workshop" },
                Text = "buy now"
            }
            : null
    };

    public sealed record Case(string Command, string Text, string[] Args, bool Reply, bool MentionsUser)
    {
        public override string ToString() => Command;
    }

    private static IEnumerable<Case> Cases() =>
    [
        new("unban", "/unban", [], Reply: true, MentionsUser: true),
        new("mute", "/mute 5m", ["5m"], Reply: true, MentionsUser: true),
        new("tempban", "/tempban 1h", ["1h"], Reply: true, MentionsUser: true),
        new("mystatus", "/mystatus", [], Reply: false, MentionsUser: false),
        new("delete", "/delete", [], Reply: true, MentionsUser: false),
        new("help", "/help", [], Reply: false, MentionsUser: false),
    ];

    private IBotCommand Build(string command) => command switch
    {
        "unban" => new UnbanCommand(NullLogger<UnbanCommand>.Instance, _moderation, _identities, _config),
        "mute" => new MuteCommand(NullLogger<MuteCommand>.Instance, _provider, _moderation, _identities, _config),
        "tempban" => new TempBanCommand(NullLogger<TempBanCommand>.Instance, _provider, _moderation, _identities, _config),
        // In a group, /mystatus answers by DM and posts a pointer when the DM can't be sent.
        "mystatus" => new MyStatusCommand(_users, NoWarnings(), FailedDm(), BotNamed("tga_bot")),
        "delete" => new DeleteCommand(NullLogger<DeleteCommand>.Instance, _provider),
        "help" => new HelpCommand(_provider),
        _ => throw new ArgumentOutOfRangeException(nameof(command), command, null)
    };

    private static IUserActionsRepository NoWarnings()
    {
        var actions = Substitute.For<IUserActionsRepository>();
        actions.GetActiveActionsAsync(Arg.Any<long>(), Arg.Any<UserActionType>(), Arg.Any<CancellationToken>())
            .Returns(new List<UserActionRecord>());
        return actions;
    }

    private static INotificationOrchestrator FailedDm()
    {
        var orchestrator = Substitute.For<INotificationOrchestrator>();
        orchestrator.SendTelegramDmAsync(Arg.Any<long>(), Arg.Any<Notification>(), Arg.Any<CancellationToken>())
            .Returns(new DeliveryResult(false, "blocked"));
        return orchestrator;
    }

    private static IBotUserService BotNamed(string username)
    {
        var users = Substitute.For<IBotUserService>();
        users.GetMeAsync(Arg.Any<CancellationToken>()).Returns(new User { Id = 1, IsBot = true, Username = username });
        return users;
    }

    [TestCaseSource(nameof(Cases))]
    public async Task ChatReply_MaskingOn_MasksAFlaggedName(Case c)
    {
        _config.GetNameMaskingAsync(TestChatId, Arg.Any<CancellationToken>()).Returns(NameMasking.On);

        var result = await Build(c.Command).ExecuteAsync(InGroup(c.Text, c.Reply), c.Args, PermissionLevel.Admin, FlaggedSender);

        Assert.That(result.Message.Text, Is.Not.Empty);
        Assert.That(result.Message.Text, Does.Not.Contain("Bad Name").And.Not.Contain("Rude Admin"));
        if (c.MentionsUser)
        {
            Assert.That(result.Message.Text, Does.Contain(NameRedaction.Explicit));
            Assert.That(result.Message.Entities.Single(e => e.Type == MessageEntityType.TextMention).User!.Id,
                Is.EqualTo(TargetId));
        }
        else
        {
            Assert.That(result.Message.Text, Does.Not.Contain(NameRedaction.Explicit));
            await _config.DidNotReceiveWithAnyArgs().GetNameMaskingAsync(default, default);
        }
    }

    [TestCaseSource(nameof(Cases))]
    public async Task ChatReply_MaskingOff_ShowsTheRealNameOfAMentionedUser(Case c)
    {
        _config.GetNameMaskingAsync(TestChatId, Arg.Any<CancellationToken>()).Returns(NameMasking.Off);

        var result = await Build(c.Command).ExecuteAsync(InGroup(c.Text, c.Reply), c.Args, PermissionLevel.Admin, FlaggedSender);

        Assert.That(result.Message.Text, Does.Not.Contain(NameRedaction.Explicit));
        if (c.MentionsUser)
            Assert.That(result.Message.Text, Does.Contain("Bad Name"));
        else
            await _config.DidNotReceiveWithAnyArgs().GetNameMaskingAsync(default, default);
    }

    // A flagged name is masked only while the user is banned, so the identity resolved before the
    // action carries the pre-action verdict. The confirmation must use the post-action identity.

    [Test]
    public async Task TempBanConfirmation_UsesTheIdentityResolvedAfterTheBan()
    {
        _config.GetNameMaskingAsync(TestChatId, Arg.Any<CancellationToken>()).Returns(NameMasking.On);
        _identities.ResolveAsync(TargetId, Arg.Any<CancellationToken>()).Returns(
            UserIdentity.ForTest(TargetId, "Promo", "Spammer", verdict: NameVerdict.Clean),
            UserIdentity.ForTest(TargetId, "Promo", "Spammer", verdict: NameVerdict.Promotional));

        var result = await Build("tempban").ExecuteAsync(
            InGroup("/tempban 1h", reply: true), ["1h"], PermissionLevel.Admin, FlaggedSender);

        Assert.That(result.Message.Text, Does.Contain(NameRedaction.Spam));
        Assert.That(result.Message.Text, Does.Not.Contain("Promo Spammer"));
    }

    [Test]
    public async Task UnbanConfirmation_UsesTheIdentityResolvedAfterTheUnban()
    {
        _config.GetNameMaskingAsync(TestChatId, Arg.Any<CancellationToken>()).Returns(NameMasking.On);
        _identities.ResolveAsync(TargetId, Arg.Any<CancellationToken>()).Returns(
            UserIdentity.ForTest(TargetId, "Promo", "Spammer", verdict: NameVerdict.Promotional),
            UserIdentity.ForTest(TargetId, "Promo", "Spammer", verdict: NameVerdict.Clean));

        var result = await Build("unban").ExecuteAsync(
            InGroup("/unban", reply: true), [], PermissionLevel.Admin, FlaggedSender);

        Assert.That(result.Message.Text, Does.Contain("Promo Spammer"));
        Assert.That(result.Message.Text, Does.Not.Contain(NameRedaction.Spam));
    }
}
