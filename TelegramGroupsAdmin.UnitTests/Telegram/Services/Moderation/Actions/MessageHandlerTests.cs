using System.Text.Json;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Infrastructure;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.Moderation.Actions;

/// <summary>
/// Unit tests for BotModerationMessageHandler.
/// Tests domain logic for message operations (backfill and deletion).
/// </summary>
[TestFixture]
public class MessageHandlerTests
{
    private IMessageHistoryRepository _mockMessageHistoryRepository = null!;
    private IMessageQueryService _mockMessageQueryService = null!;
    private IMessageBackfillService _mockMessageBackfillService = null!;
    private IBotMessageService _mockBotMessageService = null!;
    private IJobScheduler _mockJobScheduler = null!;
    private ILogger<BotModerationMessageHandler> _mockLogger = null!;
    private BotModerationMessageHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _mockMessageHistoryRepository = Substitute.For<IMessageHistoryRepository>();
        _mockMessageQueryService = Substitute.For<IMessageQueryService>();
        _mockMessageBackfillService = Substitute.For<IMessageBackfillService>();
        _mockBotMessageService = Substitute.For<IBotMessageService>();
        _mockJobScheduler = Substitute.For<IJobScheduler>();
        _mockLogger = Substitute.For<ILogger<BotModerationMessageHandler>>();

        _handler = new BotModerationMessageHandler(
            _mockMessageHistoryRepository,
            _mockMessageQueryService,
            _mockMessageBackfillService,
            _mockBotMessageService,
            _mockJobScheduler,
            _mockLogger);
    }

    #region EnsureExistsAsync Tests

    [Test]
    public async Task EnsureExistsAsync_MessageAlreadyExists_ReturnsAlreadyExists()
    {
        // Arrange
        const long messageId = 42L;
        const long chatId = -100123456789L;

        _mockMessageHistoryRepository.GetMessageAsync(messageId, Arg.Any<CancellationToken>())
            .Returns(CreateTestMessageRecord(messageId, chatId));

        // Act
        var result = await _handler.EnsureExistsAsync(messageId, ChatIdentity.FromId(chatId));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.WasBackfilled, Is.False, "Should not be backfilled since it already exists");
        });

        // Verify backfill was NOT called
        await _mockMessageBackfillService.DidNotReceive().BackfillIfMissingAsync(
            Arg.Any<long>(),
            Arg.Any<long>(),
            Arg.Any<Message>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task EnsureExistsAsync_MessageMissingWithTelegramMessage_BackfillsSuccessfully()
    {
        // Arrange
        const long messageId = 42L;
        const long chatId = -100123456789L;
        var telegramMessage = CreateTestMessage(messageId, chatId);

        _mockMessageHistoryRepository.GetMessageAsync(messageId, Arg.Any<CancellationToken>())
            .Returns((MessageRecord?)null);

        _mockMessageBackfillService.BackfillIfMissingAsync(
                messageId, chatId, telegramMessage, Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var result = await _handler.EnsureExistsAsync(messageId, ChatIdentity.FromId(chatId), telegramMessage);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.WasBackfilled, Is.True);
        });

        // Verify backfill was called
        await _mockMessageBackfillService.Received(1).BackfillIfMissingAsync(
            messageId, chatId, telegramMessage, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task EnsureExistsAsync_MessageMissingNoTelegramMessage_ReturnsNotFound()
    {
        // Arrange
        const long messageId = 42L;
        const long chatId = -100123456789L;

        _mockMessageHistoryRepository.GetMessageAsync(messageId, Arg.Any<CancellationToken>())
            .Returns((MessageRecord?)null);

        // Act - No telegramMessage provided
        var result = await _handler.EnsureExistsAsync(messageId, ChatIdentity.FromId(chatId), telegramMessage: null);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.WasBackfilled, Is.False);
            Assert.That(result.ErrorMessage, Is.Not.Null);
        });
    }

    [Test]
    public async Task EnsureExistsAsync_BackfillFails_ReturnsNotFound()
    {
        // Arrange
        const long messageId = 42L;
        const long chatId = -100123456789L;
        var telegramMessage = CreateTestMessage(messageId, chatId);

        _mockMessageHistoryRepository.GetMessageAsync(messageId, Arg.Any<CancellationToken>())
            .Returns((MessageRecord?)null);

        _mockMessageBackfillService.BackfillIfMissingAsync(
                messageId, chatId, telegramMessage, Arg.Any<CancellationToken>())
            .Returns(false); // Backfill failed

        // Act
        var result = await _handler.EnsureExistsAsync(messageId, ChatIdentity.FromId(chatId), telegramMessage);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.WasBackfilled, Is.False);
        });
    }

    #endregion

    #region DeleteAsync Tests

    [Test]
    public async Task DeleteAsync_SuccessfulDeletion_ReturnsSuccessWithMessageDeleted()
    {
        // Arrange
        const long chatId = -100123456789L;
        const long messageId = 42L;
        var executor = Actor.FromSystem("SpamDetection");

        // Act
        var result = await _handler.DeleteAsync(ChatIdentity.FromId(chatId), messageId, executor);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.MessageDeleted, Is.True);
        });

        // Verify deletion was called with correct parameters
        await _mockBotMessageService.Received(1).DeleteAndMarkMessageAsync(
            chatId,
            (int)messageId,
            "moderation_action",
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeleteAsync_DeletionFails_ReturnsFailure()
    {
        // Arrange - Message deletion fails (let boss decide what to do)
        const long chatId = -100123456789L;
        const long messageId = 42L;
        var executor = Actor.FromTelegramUser(999, "Admin");

        _mockBotMessageService.DeleteAndMarkMessageAsync(
                chatId, (int)messageId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Message not found"));

        // Act
        var result = await _handler.DeleteAsync(ChatIdentity.FromId(chatId), messageId, executor);

        // Assert - Worker reports failure, boss decides what to do
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.MessageDeleted, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("Message not found"));
        });
    }

    [Test]
    public async Task DeleteAsync_DifferentExecutors_AllCallDeleteCorrectly()
    {
        // Arrange
        const long chatId = -100123456789L;
        const long messageId = 42L;
        var executors = new[]
        {
            Actor.FromSystem("AutoMod"),
            Actor.FromTelegramUser(999, "TgAdmin"),
            Actor.FromWebUser("web-user", "admin@test.com")
        };

        // Act & Assert
        foreach (var executor in executors)
        {
            var result = await _handler.DeleteAsync(ChatIdentity.FromId(chatId), messageId, executor);
            Assert.That(result.Success, Is.True, $"Delete should succeed for {executor.Type}");
        }
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a test Telegram Message using JSON deserialization.
    /// Telegram.Bot.Types.Message uses init-only properties, so we use JSON to construct valid instances.
    /// </summary>
    private static Message CreateTestMessage(long messageId, long chatId)
    {
        var json = $$"""
        {
            "message_id": {{messageId}},
            "date": {{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}},
            "chat": {
                "id": {{chatId}},
                "type": "supergroup"
            },
            "from": {
                "id": 12345,
                "is_bot": false,
                "first_name": "Test"
            },
            "text": "Test message"
        }
        """;
        return JsonSerializer.Deserialize<Message>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;
    }

    /// <summary>
    /// Creates a test MessageRecord with all required parameters.
    /// </summary>
    private static MessageRecord CreateTestMessageRecord(long messageId, long chatId)
    {
        return new MessageRecord(
            MessageId: messageId,
            UserId: 12345L,
            UserName: "testuser",
            FirstName: "Test",
            LastName: "User",
            ChatId: chatId,
            Timestamp: DateTimeOffset.UtcNow,
            MessageText: "Test message",
            PhotoFileId: null,
            PhotoFileSize: null,
            Urls: null,
            EditDate: null,
            ContentHash: null,
            ChatName: "Test Chat",
            PhotoLocalPath: null,
            PhotoThumbnailPath: null,
            ChatIconPath: null,
            UserPhotoPath: null,
            DeletedAt: null,
            DeletionSource: null,
            ReplyToMessageId: null,
            ReplyToUser: null,
            ReplyToText: null,
            MediaType: null,
            MediaFileId: null,
            MediaFileSize: null,
            MediaFileName: null,
            MediaMimeType: null,
            MediaLocalPath: null,
            MediaDuration: null,
            Translation: null,
            ContentCheckSkipReason: ContentCheckSkipReason.NotSkipped
        );
    }

    #endregion
}
