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
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;
using TelegramGroupsAdmin.Telegram.Services.Identity;
using TelegramGroupsAdmin.Telegram.Services.Moderation;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.BotCommands.Commands;

/// <summary>
/// Moderation commands act on the target identity resolved by id (not the names the reply carries)
/// and record the pipeline's sender as the executor.
/// </summary>
[TestFixture]
public class ModerationCommandIdentityTests
{
    private const long TestChatId = -100999888L;
    private const long AdminId = 1001L;
    private const long TargetId = 7L;

    private static readonly UserIdentity Sender = UserIdentity.ForTest(AdminId, "Alex", "Admin");
    private static readonly UserIdentity Target = UserIdentity.ForTest(TargetId, "Current", username: "current_name");

    private IUserIdentityService _identities = null!;
    private IConfigService _config = null!;
    private IBotModerationService _moderation = null!;
    private ITelegramUserRepository _users = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _identities = Substitute.For<IUserIdentityService>();
        _identities.ResolveAsync(TargetId, Arg.Any<CancellationToken>()).Returns(Target);
        _config = Substitute.For<IConfigService>();

        var success = new ModerationResult { Success = true, ChatsAffected = 1 };
        _moderation = Substitute.For<IBotModerationService>();
        _moderation.BanUserAsync(Arg.Any<BanIntent>(), Arg.Any<CancellationToken>()).Returns(success);
        _moderation.RestrictUserAsync(Arg.Any<RestrictIntent>(), Arg.Any<CancellationToken>()).Returns(success);
        _moderation.TempBanUserAsync(Arg.Any<TempBanIntent>(), Arg.Any<CancellationToken>()).Returns(success);
        _moderation.UnbanUserAsync(Arg.Any<UnbanIntent>(), Arg.Any<CancellationToken>()).Returns(success);
        _moderation.TrustUserAsync(Arg.Any<TrustIntent>(), Arg.Any<CancellationToken>()).Returns(success);
        _moderation.MarkAsSpamAndBanAsync(Arg.Any<SpamBanIntent>(), Arg.Any<CancellationToken>()).Returns(success);

        _users = Substitute.For<ITelegramUserRepository>();

        var services = new ServiceCollection();
        services.AddScoped(_ => Substitute.For<IChatAdminsRepository>());
        services.AddScoped(_ => _users);
        _provider = services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown() => _provider.Dispose();

    private static Message ReplyTo(string text) => new()
    {
        Id = 50,
        From = new User { Id = AdminId, FirstName = "Stale admin" },
        Chat = new Chat { Id = TestChatId, Type = ChatType.Supergroup, Title = "Workshop" },
        Text = text,
        ReplyToMessage = new Message
        {
            Id = 49,
            From = new User { Id = TargetId, FirstName = "Stale", Username = "stale_name" },
            Chat = new Chat { Id = TestChatId, Type = ChatType.Supergroup, Title = "Workshop" },
            Text = "buy now"
        }
    };

    private static bool FromSender(Actor executor) =>
        executor.TelegramUserId == AdminId && executor.DisplayName == Sender.DisplayName;

    [Test]
    public async Task Ban_reply_bans_resolved_target_as_sender()
    {
        var command = new BanCommand(NullLogger<BanCommand>.Instance, _provider, _moderation,
            Substitute.For<IUserMessagingService>(), Substitute.For<IBotMessageService>(), _identities);

        await command.ExecuteAsync(ReplyTo("/ban"), [], PermissionLevel.Admin, Sender);

        await _moderation.Received(1).BanUserAsync(
            Arg.Is<BanIntent>(i => i!.User == Target && FromSender(i.Executor)), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Ban_by_user_id_bans_resolved_identity_not_the_stored_row()
    {
        _users.GetByTelegramIdAsync(TargetId, Arg.Any<CancellationToken>()).Returns(new TelegramUser(
            TelegramUserId: TargetId, Username: "row_name", FirstName: "Row", LastName: null,
            UserPhotoPath: null, PhotoHash: null, PhotoFileUniqueId: null,
            IsBot: false, IsTrusted: false, IsBanned: false, KickCount: 0, BotDmEnabled: false,
            FirstSeenAt: DateTimeOffset.UtcNow, LastSeenAt: DateTimeOffset.UtcNow,
            CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: DateTimeOffset.UtcNow));
        var command = new BanCommand(NullLogger<BanCommand>.Instance, _provider, _moderation,
            Substitute.For<IUserMessagingService>(), Substitute.For<IBotMessageService>(), _identities);
        var message = ReplyTo($"/ban {TargetId}");
        message.ReplyToMessage = null;

        await command.ExecuteAsync(message, [TargetId.ToString()], PermissionLevel.Admin, Sender);

        await _moderation.Received(1).BanUserAsync(
            Arg.Is<BanIntent>(i => i!.User == Target && FromSender(i.Executor)), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Mute_restricts_resolved_target_as_sender()
    {
        var command = new MuteCommand(NullLogger<MuteCommand>.Instance, _provider, _moderation, _identities, _config);

        var result = await command.ExecuteAsync(ReplyTo("/mute 5m"), ["5m"], PermissionLevel.Admin, Sender);

        await _moderation.Received(1).RestrictUserAsync(
            Arg.Is<RestrictIntent>(i => i!.User == Target && FromSender(i.Executor)), Arg.Any<CancellationToken>());
        AssertMentionsTarget(result.Message, "🔇 User Current muted");
    }

    [Test]
    public async Task TempBan_bans_resolved_target_as_sender()
    {
        var command = new TempBanCommand(NullLogger<TempBanCommand>.Instance, _provider, _moderation, _identities, _config);

        var result = await command.ExecuteAsync(ReplyTo("/tempban 1h"), ["1h"], PermissionLevel.Admin, Sender);

        await _moderation.Received(1).TempBanUserAsync(
            Arg.Is<TempBanIntent>(i => i!.User == Target && FromSender(i.Executor)), Arg.Any<CancellationToken>());
        AssertMentionsTarget(result.Message, "⏱️ User Current temp banned");
    }

    [Test]
    public async Task Unban_unbans_resolved_target_as_sender()
    {
        var command = new UnbanCommand(NullLogger<UnbanCommand>.Instance, _moderation, _identities, _config);

        var result = await command.ExecuteAsync(ReplyTo("/unban"), [], PermissionLevel.Admin, Sender);

        await _moderation.Received(1).UnbanUserAsync(
            Arg.Is<UnbanIntent>(i => i!.User == Target && FromSender(i.Executor)), Arg.Any<CancellationToken>());
        AssertMentionsTarget(result.Message, "✅ User Current unbanned");
    }

    [Test]
    public async Task Trust_trusts_resolved_target_as_sender()
    {
        var command = new TrustCommand(NullLogger<TrustCommand>.Instance, _provider, _moderation, _identities, _config);

        var result = await command.ExecuteAsync(ReplyTo("/trust"), [], PermissionLevel.Admin, Sender);

        await _moderation.Received(1).TrustUserAsync(
            Arg.Is<TrustIntent>(i => i!.User == Target && FromSender(i.Executor)), Arg.Any<CancellationToken>());
        AssertMentionsTarget(result.Message, "✅ User Current marked as trusted");
    }

    [Test]
    public async Task Trust_confirmation_masks_a_flagged_target_name()
    {
        var flagged = UserIdentity.ForTest(TargetId, "Promo", username: "buy_now", verdict: NameVerdict.Promotional);
        _identities.ResolveAsync(TargetId, Arg.Any<CancellationToken>()).Returns(flagged);
        _config.GetNameMaskingAsync(TestChatId, Arg.Any<CancellationToken>()).Returns(NameMasking.On);
        var command = new TrustCommand(NullLogger<TrustCommand>.Instance, _provider, _moderation, _identities, _config);

        var result = await command.ExecuteAsync(ReplyTo("/trust"), [], PermissionLevel.Admin, Sender);

        Assert.That(result.Message.Text, Does.StartWith("✅ User " + NameRedaction.Spam + " marked as trusted"));
        Assert.That(result.Message.Text, Does.Not.Contain("Promo").And.Not.Contain("buy_now"));
    }

    /// <summary>The confirmation names the target with a clickable mention (masking Off by default here).</summary>
    private static void AssertMentionsTarget(TelegramMessage message, string expectedStart)
    {
        Assert.That(message.Text, Does.StartWith(expectedStart));
        Assert.That(message.Text, Does.Not.Contain("@current_name"));
        Assert.That(message.Entities.Single(e => e.Type == MessageEntityType.TextMention).User!.Id, Is.EqualTo(TargetId));
    }

    [Test]
    public async Task Spam_bans_resolved_target_as_sender()
    {
        var command = new SpamCommand(NullLogger<SpamCommand>.Instance, _provider, _moderation, _identities);

        await command.ExecuteAsync(ReplyTo("/spam"), [], PermissionLevel.Admin, Sender);

        await _moderation.Received(1).MarkAsSpamAndBanAsync(
            Arg.Is<SpamBanIntent>(i => i!.User == Target && FromSender(i.Executor)), Arg.Any<CancellationToken>());
    }
}
