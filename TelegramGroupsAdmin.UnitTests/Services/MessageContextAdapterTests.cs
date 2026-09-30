using Microsoft.Extensions.Logging;
using NSubstitute;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Services;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.UnitTests.Services;

[TestFixture]
public class MessageContextAdapterTests
{
    private IMessageHistoryRepository _repository = null!;
    private MessageContextAdapter _adapter = null!;

    [SetUp]
    public void Setup()
    {
        _repository = Substitute.For<IMessageHistoryRepository>();
        _repository
            .GetRecentMessagesWithVerdictAsync(Arg.Any<long>(), Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<RecentMessageVerdict>>([]));
        _adapter = new MessageContextAdapter(_repository, Substitute.For<ILogger<MessageContextAdapter>>());
    }

    [Test]
    public async Task GetRecentMessagesAsync_ForwardsExcludedMessageIdToRepository()
    {
        await _adapter.GetRecentMessagesAsync(ChatIdentity.FromId(-100123), count: 3, excludeMessageId: 77310);

        await _repository.Received(1).GetRecentMessagesWithVerdictAsync(-100123, 3, 77310, Arg.Any<CancellationToken>());
    }
}
