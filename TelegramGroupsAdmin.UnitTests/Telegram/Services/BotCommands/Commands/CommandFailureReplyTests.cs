using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.BotCommands;
using TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;
using TelegramGroupsAdmin.Telegram.Services.Identity;
using TelegramGroupsAdmin.Telegram.Services.Moderation;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.BotCommands.Commands;

/// <summary>
/// A command's reply is posted into the group chat, so a failure must never put exception text
/// there. The reply is generic; the full exception goes to the log.
/// </summary>
[TestFixture]
public class CommandFailureReplyTests
{
    private const long TestChatId = -100999888L;
    private const long AdminId = 1001L;
    private const long TargetId = 7L;
    private const string Secret = "Host=db;Password=hunter2";

    private static readonly UserIdentity Sender = UserIdentity.ForTest(AdminId, "Alex", "Admin");

    private IUserIdentityService _identities = null!;
    private IConfigService _config = null!;
    private IBotModerationService _moderation = null!;
    private IBotMessageService _messages = null!;
    private ITelegramUserRepository _users = null!;
    private ServiceProvider _provider = null!;
    private InvalidOperationException _boom = null!;

    [SetUp]
    public void SetUp()
    {
        _boom = new InvalidOperationException(Secret);
        _identities = Substitute.For<IUserIdentityService>();
        _identities.ResolveAsync(TargetId, Arg.Any<CancellationToken>())
            .Returns(UserIdentity.ForTest(TargetId, "Target"));
        _config = Substitute.For<IConfigService>();

        _moderation = Substitute.For<IBotModerationService>();
        _moderation.BanUserAsync(Arg.Any<BanIntent>(), Arg.Any<CancellationToken>()).ThrowsAsync(_boom);
        _moderation.RestrictUserAsync(Arg.Any<RestrictIntent>(), Arg.Any<CancellationToken>()).ThrowsAsync(_boom);
        _moderation.TempBanUserAsync(Arg.Any<TempBanIntent>(), Arg.Any<CancellationToken>()).ThrowsAsync(_boom);
        _moderation.UnbanUserAsync(Arg.Any<UnbanIntent>(), Arg.Any<CancellationToken>()).ThrowsAsync(_boom);
        _moderation.WarnUserAsync(Arg.Any<WarnIntent>(), Arg.Any<CancellationToken>()).ThrowsAsync(_boom);

        _messages = Substitute.For<IBotMessageService>();
        _messages.DeleteAndMarkMessageAsync(Arg.Any<long>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(_boom);

        _users = Substitute.For<ITelegramUserRepository>();

        var services = new ServiceCollection();
        services.AddScoped(_ => Substitute.For<IChatAdminsRepository>());
        services.AddScoped(_ => _users);
        services.AddScoped(_ => _messages);
        _provider = services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown() => _provider.Dispose();

    private static Message ReplyTo(string text) => new()
    {
        Id = 50,
        From = new User { Id = AdminId, FirstName = "Alex" },
        Chat = new Chat { Id = TestChatId, Type = ChatType.Supergroup, Title = "Workshop" },
        Text = text,
        ReplyToMessage = new Message
        {
            Id = 49,
            From = new User { Id = TargetId, FirstName = "Target" },
            Chat = new Chat { Id = TestChatId, Type = ChatType.Supergroup, Title = "Workshop" },
            Text = "buy now"
        }
    };

    public sealed record Case(string Name, string Text, string[] Args, string Reply)
    {
        public override string ToString() => Name;
    }

    private static IEnumerable<Case> Cases() =>
    [
        new("ban", "/ban", [], "❌ Failed to ban user."),
        new("mute", "/mute 5m", ["5m"], "❌ Failed to mute user."),
        new("tempban", "/tempban 1h", ["1h"], "❌ Failed to temp ban user."),
        new("unban", "/unban", [], "❌ Failed to unban user."),
        new("warn", "/warn spam", ["spam"], "❌ Failed to issue warning."),
        new("delete", "/delete", [], "❌ Failed to delete message."),
    ];

    private (IBotCommand Command, ILogger Logger) Build(string name)
    {
        switch (name)
        {
            case "ban":
                var banLog = Substitute.For<ILogger<BanCommand>>();
                return (new BanCommand(banLog, _provider, _moderation, Substitute.For<IUserMessagingService>(),
                    _messages, _identities), banLog);
            case "mute":
                var muteLog = Substitute.For<ILogger<MuteCommand>>();
                return (new MuteCommand(muteLog, _provider, _moderation, _identities, _config), muteLog);
            case "tempban":
                var tempLog = Substitute.For<ILogger<TempBanCommand>>();
                return (new TempBanCommand(tempLog, _provider, _moderation, _identities, _config), tempLog);
            case "unban":
                var unbanLog = Substitute.For<ILogger<UnbanCommand>>();
                return (new UnbanCommand(unbanLog, _moderation, _identities, _config), unbanLog);
            case "warn":
                var warnLog = Substitute.For<ILogger<WarnCommand>>();
                return (new WarnCommand(warnLog, _provider, _moderation, Substitute.For<IUserMessagingService>(),
                    _identities, _config), warnLog);
            case "trust":
            case "untrust":
                var trustLog = Substitute.For<ILogger<TrustCommand>>();
                return (new TrustCommand(trustLog, _provider, _moderation, _identities, _config), trustLog);
            case "spam":
                var spamLog = Substitute.For<ILogger<SpamCommand>>();
                return (new SpamCommand(spamLog, _provider, _moderation, _identities), spamLog);
            case "delete":
                var deleteLog = Substitute.For<ILogger<DeleteCommand>>();
                return (new DeleteCommand(deleteLog, _provider), deleteLog);
            default:
                throw new ArgumentOutOfRangeException(nameof(name), name, null);
        }
    }

    [TestCaseSource(nameof(Cases))]
    public async Task CommandThrows_RepliesWithoutExceptionText_AndLogsTheException(Case c)
    {
        var (command, logger) = Build(c.Name);

        var result = await command.ExecuteAsync(ReplyTo(c.Text), c.Args, PermissionLevel.Admin, Sender);

        Assert.That(result.Message.Text, Is.EqualTo(c.Reply));
        Assert.That(result.Message.Text, Does.Not.Contain("hunter2"));
        logger.Received(1).Log(LogLevel.Error, Arg.Any<EventId>(), Arg.Any<object>(), _boom,
            Arg.Any<Func<object, Exception?, string>>());
    }

    private static IEnumerable<Case> FailedResultCases() =>
    [
        new("ban", "/ban", [], "❌ Failed to ban user."),
        new("mute", "/mute 5m", ["5m"], "❌ Failed to mute user."),
        new("tempban", "/tempban 1h", ["1h"], "❌ Failed to temp ban user."),
        new("unban", "/unban", [], "❌ Failed to unban user."),
        new("warn", "/warn spam", ["spam"], "❌ Failed to issue warning."),
        new("trust", "/trust", [], "❌ Failed to trust user."),
        new("untrust", "/trust", [], "❌ Failed to untrust user."),
        new("spam", "/spam", [], "❌ Failed to process spam action."),
    ];

    /// <summary>
    /// The moderation service reports a failure whose ErrorMessage can carry exception text from
    /// the handler. The chat reply stays generic; the ErrorMessage goes to the log at Warning.
    /// </summary>
    [TestCaseSource(nameof(FailedResultCases))]
    public async Task ModerationFails_RepliesWithoutErrorMessage_AndLogsWarning(Case c)
    {
        var failed = ModerationResult.Failed(Secret);
        _moderation.BanUserAsync(Arg.Any<BanIntent>(), Arg.Any<CancellationToken>()).Returns(failed);
        _moderation.RestrictUserAsync(Arg.Any<RestrictIntent>(), Arg.Any<CancellationToken>()).Returns(failed);
        _moderation.TempBanUserAsync(Arg.Any<TempBanIntent>(), Arg.Any<CancellationToken>()).Returns(failed);
        _moderation.UnbanUserAsync(Arg.Any<UnbanIntent>(), Arg.Any<CancellationToken>()).Returns(failed);
        _moderation.WarnUserAsync(Arg.Any<WarnIntent>(), Arg.Any<CancellationToken>()).Returns(failed);
        _moderation.TrustUserAsync(Arg.Any<TrustIntent>(), Arg.Any<CancellationToken>()).Returns(failed);
        _moderation.UntrustUserAsync(Arg.Any<UntrustIntent>(), Arg.Any<CancellationToken>()).Returns(failed);
        _moderation.MarkAsSpamAndBanAsync(Arg.Any<SpamBanIntent>(), Arg.Any<CancellationToken>()).Returns(failed);
        _users.IsTrustedAsync(TargetId, Arg.Any<CancellationToken>()).Returns(c.Name == "untrust");
        var (command, logger) = Build(c.Name);

        var result = await command.ExecuteAsync(ReplyTo(c.Text), c.Args, PermissionLevel.Admin, Sender);

        Assert.That(result.Message.Text, Is.EqualTo(c.Reply));
        Assert.That(result.Message.Text, Does.Not.Contain("hunter2"));
        logger.Received(1).Log(LogLevel.Warning, Arg.Any<EventId>(),
            Arg.Is<object>(state => state!.ToString()!.Contains(Secret)), null,
            Arg.Any<Func<object, Exception?, string>>());
    }
}
