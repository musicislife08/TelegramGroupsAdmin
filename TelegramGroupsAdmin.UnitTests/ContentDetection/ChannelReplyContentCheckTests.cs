using Microsoft.Extensions.Logging;
using NSubstitute;
using TelegramGroupsAdmin.ContentDetection.Checks;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.UnitTests.ContentDetection;

[TestFixture]
public class ChannelReplyContentCheckTests
{
    private ChannelReplyContentCheckV2 _check = null!;

    [SetUp]
    public void Setup()
    {
        var logger = Substitute.For<ILogger<ChannelReplyContentCheckV2>>();
        _check = new ChannelReplyContentCheckV2(logger);
    }

    [Test]
    public void CheckName_Returns_ChannelReply()
    {
        Assert.That(_check.CheckName, Is.EqualTo(CheckName.ChannelReply));
    }

    [Test]
    public void ShouldExecute_Returns_True_When_IsReplyToChannelPost()
    {
        var request = CreateRequest(isReplyToChannelPost: true);

        Assert.That(_check.ShouldExecute(request), Is.True);
    }

    [Test]
    public void ShouldExecute_Returns_False_When_Not_ReplyToChannelPost()
    {
        var request = CreateRequest(isReplyToChannelPost: false);

        Assert.That(_check.ShouldExecute(request), Is.False);
    }

    [Test]
    public void ShouldExecute_Returns_False_For_Trusted_User()
    {
        var request = CreateRequest(isReplyToChannelPost: true, isUserTrusted: true);

        Assert.That(_check.ShouldExecute(request), Is.False);
    }

    [Test]
    public void ShouldExecute_Returns_False_For_Admin_User()
    {
        var request = CreateRequest(isReplyToChannelPost: true, isUserAdmin: true);

        Assert.That(_check.ShouldExecute(request), Is.False);
    }

    [Test]
    public async Task CheckAsync_Returns_Score_When_ReplyToChannelPost()
    {
        var request = new ChannelReplyCheckRequest
        {
            Message = "Buy cheap stuff!",
            User = UserIdentity.FromId(123),
            Chat = ChatIdentity.FromId(-1001234567890),
            CancellationToken = CancellationToken.None
        };

        var result = await _check.CheckAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.CheckName, Is.EqualTo(CheckName.ChannelReply));
            Assert.That(result.Score, Is.EqualTo(ScoringConstants.ScoreChannelReply));
            Assert.That(result.Abstained, Is.False);
            Assert.That(result.ProcessingTimeMs, Is.GreaterThanOrEqualTo(0));
        }
    }

    private static ContentCheckRequest CreateRequest(
        bool isReplyToChannelPost = false,
        bool isUserTrusted = false,
        bool isUserAdmin = false) =>
        new()
        {
            Message = "Test message",
            User = UserIdentity.FromId(123),
            Chat = ChatIdentity.FromId(-1001234567890),
            IsUserTrusted = isUserTrusted,
            IsUserAdmin = isUserAdmin,
            Metadata = new ContentCheckMetadata
            {
                IsReplyToChannelPost = isReplyToChannelPost
            }
        };
}
