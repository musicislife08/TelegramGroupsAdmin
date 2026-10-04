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
    public async Task Backfill_StoresTheAuthorResolvedById()
    {
        var resolved = UserIdentity.ForTest(AuthorId, "Current");
        _identities.ResolveAsync(AuthorId, Arg.Any<CancellationToken>()).Returns(resolved);

        var backfilled = await _sut.BackfillIfMissingAsync(42, ChatId, Missed(new User { Id = AuthorId, FirstName = "Stale" }));

        Assert.That(backfilled, Is.True);
        await _messages.Received(1).InsertMessageAsync(
            Arg.Is<MessageRecord>(m => m!.User == resolved), Arg.Any<CancellationToken>());
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
