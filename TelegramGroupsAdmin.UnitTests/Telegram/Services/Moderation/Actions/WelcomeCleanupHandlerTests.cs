using Microsoft.Extensions.Logging;
using NSubstitute;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Results;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.Moderation.Actions;

/// <summary>
/// Unit tests for WelcomeCleanupHandler.
/// Deletes the stranded welcome/teaser message; never touches the response state,
/// which each caller owns.
/// </summary>
[TestFixture]
public class WelcomeCleanupHandlerTests
{
    private const long ChatAId = -100111L;
    private const long ChatBId = -100222L;
    private static readonly UserIdentity TestUser = new(555L, "Test", null, "testuser");
    private static readonly Actor TestExecutor = Actor.AutoDetection;

    private IWelcomeResponsesRepository _welcomeRepository = null!;
    private IBotModerationMessageHandler _messageHandler = null!;
    private WelcomeCleanupHandler _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _welcomeRepository = Substitute.For<IWelcomeResponsesRepository>();
        _messageHandler = Substitute.For<IBotModerationMessageHandler>();
        _messageHandler
            .DeleteAsync(Arg.Any<ChatIdentity>(), Arg.Any<int>(), Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(DeleteResult.Succeeded());

        _sut = new WelcomeCleanupHandler(
            _welcomeRepository,
            _messageHandler,
            Substitute.For<ILogger<WelcomeCleanupHandler>>());
    }

    private static WelcomeResponse Response(long chatId, int welcomeMessageId) => new(
        Id: 1,
        ChatId: chatId,
        UserId: TestUser.Id,
        Username: "testuser",
        WelcomeMessageId: welcomeMessageId,
        Response: WelcomeResponseType.Accepted,
        RespondedAt: DateTimeOffset.UtcNow,
        DmSent: false,
        DmFallback: false,
        CreatedAt: DateTimeOffset.UtcNow,
        TimeoutJobId: null);

    [Test]
    public async Task DeleteStrandedWelcomeMessagesAsync_NoChat_DeletesAcrossEveryChat()
    {
        _welcomeRepository.GetByUserAsync(TestUser.Id, Arg.Any<CancellationToken>())
            .Returns([Response(ChatAId, 100), Response(ChatBId, 200)]);

        var deleted = await _sut.DeleteStrandedWelcomeMessagesAsync(TestUser, chat: null, TestExecutor);

        Assert.That(deleted, Is.EqualTo(2));
        await _messageHandler.Received(1).DeleteAsync(
            Arg.Is<ChatIdentity>(c => c!.Id == ChatAId), 100, TestExecutor, Arg.Any<CancellationToken>());
        await _messageHandler.Received(1).DeleteAsync(
            Arg.Is<ChatIdentity>(c => c!.Id == ChatBId), 200, TestExecutor, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeleteStrandedWelcomeMessagesAsync_WithChat_DeletesOnlyThatChat()
    {
        _welcomeRepository.GetByUserAndChatAsync(TestUser.Id, ChatAId, Arg.Any<CancellationToken>())
            .Returns(Response(ChatAId, 100));

        var deleted = await _sut.DeleteStrandedWelcomeMessagesAsync(
            TestUser, new ChatIdentity(ChatAId, "ChatA"), TestExecutor);

        Assert.That(deleted, Is.EqualTo(1));
        await _welcomeRepository.DidNotReceive().GetByUserAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeleteStrandedWelcomeMessagesAsync_NeverWritesResponseState()
    {
        _welcomeRepository.GetByUserAsync(TestUser.Id, Arg.Any<CancellationToken>())
            .Returns([Response(ChatAId, 100)]);

        await _sut.DeleteStrandedWelcomeMessagesAsync(TestUser, chat: null, TestExecutor);

        await _welcomeRepository.DidNotReceive().UpdateResponseAsync(
            Arg.Any<long>(), Arg.Any<WelcomeResponseType>(), Arg.Any<bool>(), Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeleteStrandedWelcomeMessagesAsync_SkipsZeroMessageId()
    {
        _welcomeRepository.GetByUserAsync(TestUser.Id, Arg.Any<CancellationToken>())
            .Returns([Response(ChatAId, 0)]);

        var deleted = await _sut.DeleteStrandedWelcomeMessagesAsync(TestUser, chat: null, TestExecutor);

        Assert.That(deleted, Is.Zero);
        await _messageHandler.DidNotReceive().DeleteAsync(
            Arg.Any<ChatIdentity>(), Arg.Any<int>(), Arg.Any<Actor>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeleteStrandedWelcomeMessagesAsync_DeleteFailureResult_NotCountedAsDeleted()
    {
        // Realistic failure mode: BotModerationMessageHandler.DeleteAsync never throws — it
        // reports failure via DeleteResult.Success. This is the signal the handler must honor.
        _welcomeRepository.GetByUserAsync(TestUser.Id, Arg.Any<CancellationToken>())
            .Returns([Response(ChatAId, 100)]);
        _messageHandler
            .DeleteAsync(Arg.Any<ChatIdentity>(), Arg.Any<int>(), Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(DeleteResult.Failed("message already gone"));

        var deleted = await _sut.DeleteStrandedWelcomeMessagesAsync(TestUser, chat: null, TestExecutor);

        Assert.That(deleted, Is.Zero, "a failed DeleteResult must not be counted as a deleted message");
    }

    [Test]
    public async Task DeleteStrandedWelcomeMessagesAsync_DeleteThrows_DoesNotThrow()
    {
        // Defense-in-depth: DeleteAsync is not documented to throw, but cleanup must still
        // never fail the ban that already landed if some future implementation does.
        _welcomeRepository.GetByUserAsync(TestUser.Id, Arg.Any<CancellationToken>())
            .Returns([Response(ChatAId, 100)]);
        _messageHandler
            .DeleteAsync(Arg.Any<ChatIdentity>(), Arg.Any<int>(), Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns<DeleteResult>(_ => throw new InvalidOperationException("unexpected throw"));

        var deleted = await _sut.DeleteStrandedWelcomeMessagesAsync(TestUser, chat: null, TestExecutor);

        Assert.That(deleted, Is.Zero, "cleanup must never fail the ban that already landed");
    }

    [Test]
    public async Task DeleteStrandedWelcomeMessagesAsync_OneChatFails_RemainingChatsStillProcessed()
    {
        _welcomeRepository.GetByUserAsync(TestUser.Id, Arg.Any<CancellationToken>())
            .Returns([Response(ChatAId, 100), Response(ChatBId, 200)]);
        _messageHandler
            .DeleteAsync(Arg.Is<ChatIdentity>(c => c!.Id == ChatAId), 100, TestExecutor, Arg.Any<CancellationToken>())
            .Returns(DeleteResult.Failed("permission error"));
        _messageHandler
            .DeleteAsync(Arg.Is<ChatIdentity>(c => c!.Id == ChatBId), 200, TestExecutor, Arg.Any<CancellationToken>())
            .Returns(DeleteResult.Succeeded());

        var deleted = await _sut.DeleteStrandedWelcomeMessagesAsync(TestUser, chat: null, TestExecutor);

        Assert.That(deleted, Is.EqualTo(1), "chat B's success must still be counted despite chat A's failure");
        await _messageHandler.Received(1).DeleteAsync(
            Arg.Is<ChatIdentity>(c => c!.Id == ChatAId), 100, TestExecutor, Arg.Any<CancellationToken>());
        await _messageHandler.Received(1).DeleteAsync(
            Arg.Is<ChatIdentity>(c => c!.Id == ChatBId), 200, TestExecutor, Arg.Any<CancellationToken>());
    }
}
