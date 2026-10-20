using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Identity;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Infrastructure;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.Moderation.Infrastructure;

[TestFixture]
public class MessageBackfillServiceTests
{
    private const long ChatId = -1001L;
    private const long AuthorId = 7L;

    private IMessageHistoryRepository _messages = null!;
    private IUserIdentityService _identities = null!;
    private MessageBackfillService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _messages = Substitute.For<IMessageHistoryRepository>();
        _identities = Substitute.For<IUserIdentityService>();
        _sut = new MessageBackfillService(_messages, _identities, NullLogger<MessageBackfillService>.Instance);
    }

    private static Message Missed(User? from) => new()
    {
        Id = 42,
        Chat = new Chat { Id = ChatId, Type = ChatType.Supergroup },
        From = from,
        Date = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc),
        Text = "buy now"
    };

    [Test]
    public async Task Backfill_ObservesTheAuthorAndStoresTheObservedIdentity()
    {
        var observed = UserIdentity.ForTest(AuthorId, "Current");
        _identities.ObserveAsync(
            Arg.Is<ObservedUser>(o => o!.Id == AuthorId), Arg.Any<ProfileChangeContext>(), Arg.Any<CancellationToken>())
            .Returns(observed);
        var message = Missed(new User { Id = AuthorId, FirstName = "Stale", LastName = "Name", Username = "stale_name" });

        var backfilled = await _sut.BackfillIfMissingAsync(42, ChatId, message);

        Assert.That(backfilled, Is.True);
        await _identities.Received(1).ObserveAsync(
            Arg.Is<ObservedUser>(o => o!.Id == AuthorId && o.FirstName == "Stale" && o.LastName == "Name"
                && o.Username == "stale_name" && o.Source == ObservationSource.ReplyTarget
                && o.ObservedAt == new DateTimeOffset(message.Date, TimeSpan.Zero)),
            Arg.Is<ProfileChangeContext>(c => c!.Chat!.Id == ChatId),
            Arg.Any<CancellationToken>());
        await _identities.DidNotReceive().ResolveAsync(AuthorId, Arg.Any<CancellationToken>());
        await _messages.Received(1).InsertMessageAsync(
            Arg.Is<MessageRecord>(m => m!.User == observed), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Backfill_MessageWithoutSender_IsStoredUnderUserZero()
    {
        var unknown = UserIdentity.ForTest(0);
        _identities.ResolveAsync(0, Arg.Any<CancellationToken>()).Returns(unknown);

        var backfilled = await _sut.BackfillIfMissingAsync(42, ChatId, Missed(from: null));

        Assert.That(backfilled, Is.True);
        await _messages.Received(1).InsertMessageAsync(
            Arg.Is<MessageRecord>(m => m!.User == unknown), Arg.Any<CancellationToken>());
    }
}
