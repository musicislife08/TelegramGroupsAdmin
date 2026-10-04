using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Identity;
using TelegramGroupsAdmin.Telegram.Services.Moderation;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services;

/// <summary>
/// Banning an unauthorized bot first records the bot's names through IUserIdentityService (dated by
/// the join update, never rescanned: bots are not scanned) and bans the identity that returns.
/// </summary>
[TestFixture]
public class BotProtectionServiceTests
{
    private const long ChatId = -100777;
    private const long BotId = 888;

    private IUserIdentityService _identities = null!;
    private IBotModerationService _moderation = null!;
    private BotProtectionService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _identities = Substitute.For<IUserIdentityService>();
        _identities.ObserveAsync(Arg.Any<ObservedUser>(), Arg.Any<ProfileChangeContext>(), Arg.Any<CancellationToken>())
            .Returns(ci => UserIdentity.ForTest(ci.Arg<ObservedUser>().Id, "Stored Bot"));
        _moderation = Substitute.For<IBotModerationService>();
        _moderation.SyncBanToChatAsync(Arg.Any<SyncBanIntent>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true });

        _sut = new BotProtectionService(
            Substitute.For<IConfigService>(),
            Substitute.For<IChatAdminsRepository>(),
            _identities,
            _moderation,
            NullLogger<BotProtectionService>.Instance);
    }

    [Test]
    public async Task BanBot_ObservesBotWithUpdateDate_ThenBansTheReturnedIdentity()
    {
        var chat = new Chat { Id = ChatId, Type = ChatType.Supergroup, Title = "Group" };
        var bot = new User { Id = BotId, FirstName = "Spam", Username = "spam_bot", IsBot = true };
        var seenAt = new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

        await _sut.BanBotAsync(chat, bot, seenAt, "Not whitelisted", CancellationToken.None);

        await _identities.Received(1).ObserveAsync(
            Arg.Is<ObservedUser>(o => o!.Id == BotId && o.IsBot && o.ObservedAt == seenAt && o.Username == "spam_bot"
                && o.Source == ObservationSource.ChatMember),
            Arg.Is<ProfileChangeContext>(c => c!.Chat!.Id == ChatId),
            Arg.Any<CancellationToken>());
        await _moderation.Received(1).SyncBanToChatAsync(
            Arg.Is<SyncBanIntent>(i => i!.User.Id == BotId && i.User.FirstName == "Stored Bot" && i.Chat.Id == ChatId),
            Arg.Any<CancellationToken>());
    }
}
