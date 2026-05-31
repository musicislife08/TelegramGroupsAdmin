using Microsoft.Extensions.Logging;
using NSubstitute;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Notifications;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.Notifications;

/// <summary>
/// Unit tests for TelegramDmChannel — verifies that the channel routes to entities
/// when Notification.Telegram is set, and falls back to plain text otherwise.
/// ParseMode.Html is no longer used by this channel.
///
/// Test Coverage (3 tests):
/// - When Telegram payload is set: delegates to SendDmWithEntitiesAsync with that payload's text and entities
/// - When Telegram payload is null: delegates to SendDmWithEntitiesAsync with Message and empty entities
/// - When Telegram payload is null: does NOT call SendDmWithQueueAsync with ParseMode.Html
/// </summary>
[TestFixture]
public class TelegramDmChannelTests
{
    private IBotDmService _mockDmService = null!;
    private ILogger<TelegramDmChannel> _mockLogger = null!;
    private TelegramDmChannel _channel = null!;

    [SetUp]
    public void SetUp()
    {
        _mockDmService = Substitute.For<IBotDmService>();
        _mockLogger = Substitute.For<ILogger<TelegramDmChannel>>();

        _channel = new TelegramDmChannel(_mockLogger, _mockDmService);
    }

    [Test]
    public async Task SendAsync_WithTelegramPayload_CallsEntitiesOverloadWithPayloadTextAndEntities()
    {
        // Arrange
        const long recipientId = 12345L;
        var entities = new MessageEntity[]
        {
            new() { Type = MessageEntityType.Bold, Offset = 0, Length = 7 }
        };
        var telegramMessage = new TelegramMessage("Warning!", entities);
        var notification = new Notification("warning", "Warning!", telegramMessage);

        _mockDmService.SendDmWithEntitiesAsync(
                Arg.Any<UserIdentity>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<MessageEntity>>(),
                Arg.Any<CancellationToken>())
            .Returns(new DmDeliveryResult { DmSent = true });

        // Act
        var result = await _channel.SendAsync(recipientId.ToString(), notification);

        // Assert
        Assert.That(result.Success, Is.True);
        await _mockDmService.Received(1).SendDmWithEntitiesAsync(
            Arg.Is<UserIdentity>(u => u.Id == recipientId),
            "warning",
            "Warning!",
            Arg.Is<IReadOnlyList<MessageEntity>>(e => e.Count == 1 && e[0].Type == MessageEntityType.Bold),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendAsync_WithoutTelegramPayload_CallsEntitiesOverloadWithMessageAndEmptyEntities()
    {
        // Arrange
        const long recipientId = 99999L;
        var notification = new Notification("critical_violation", "Plain fallback text");
        // Telegram is null (default)

        _mockDmService.SendDmWithEntitiesAsync(
                Arg.Any<UserIdentity>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<MessageEntity>>(),
                Arg.Any<CancellationToken>())
            .Returns(new DmDeliveryResult { DmSent = true });

        // Act
        var result = await _channel.SendAsync(recipientId.ToString(), notification);

        // Assert — sent with plain text and NO entities
        Assert.That(result.Success, Is.True);
        await _mockDmService.Received(1).SendDmWithEntitiesAsync(
            Arg.Is<UserIdentity>(u => u.Id == recipientId),
            "critical_violation",
            "Plain fallback text",
            Arg.Is<IReadOnlyList<MessageEntity>>(e => e.Count == 0),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendAsync_WithoutTelegramPayload_NeverCallsSendDmWithQueueAsyncWithHtml()
    {
        // Arrange
        const long recipientId = 77777L;
        var notification = new Notification("warning", "Some message");

        _mockDmService.SendDmWithEntitiesAsync(
                Arg.Any<UserIdentity>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<MessageEntity>>(),
                Arg.Any<CancellationToken>())
            .Returns(new DmDeliveryResult { DmSent = true });

        // Act
        await _channel.SendAsync(recipientId.ToString(), notification);

        // Assert — ParseMode.Html is gone; the old queue overload is NOT called
        await _mockDmService.DidNotReceive().SendDmWithQueueAsync(
            Arg.Any<UserIdentity>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            ParseMode.Html,
            Arg.Any<CancellationToken>());
    }
}
