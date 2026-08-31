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
/// Unit tests for TelegramDmChannel — verifies that the channel sends the notification's
/// TelegramMessage via SendDmWithEntitiesAsync using its text and entities.
/// ParseMode.Html is no longer used by this channel.
///
/// Test Coverage (3 tests):
/// - With an entity payload: delegates to SendDmWithEntitiesAsync with that message's text and entities
/// - With a plain message (TelegramMessage.Plain): delegates with the text and empty entities
/// - With a plain message: passes empty entities to SendDmWithEntitiesAsync (no ParseMode.Html path)
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
    public async Task SendAsync_WithEntityPayload_CallsEntitiesOverloadWithMessageTextAndEntities()
    {
        // Arrange
        const long recipientId = 12345L;
        var entities = new MessageEntity[]
        {
            new() { Type = MessageEntityType.Bold, Offset = 0, Length = 7 }
        };
        var notification = new Notification("warning", new TelegramMessage("Warning!", entities));

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
            Arg.Is<UserIdentity>(u => u!.Id == recipientId),
            "warning",
            "Warning!",
            Arg.Is<IReadOnlyList<MessageEntity>>(e => e!.Count == 1 && e[0].Type == MessageEntityType.Bold),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendAsync_WithPlainMessage_CallsEntitiesOverloadWithTextAndEmptyEntities()
    {
        // Arrange
        const long recipientId = 99999L;
        var notification = new Notification("critical_violation", TelegramMessage.Plain("Plain fallback text"));

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
            Arg.Is<UserIdentity>(u => u!.Id == recipientId),
            "critical_violation",
            "Plain fallback text",
            Arg.Is<IReadOnlyList<MessageEntity>>(e => e!.Count == 0),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendAsync_WithPlainMessage_SendsPlainTextViaEntities()
    {
        // Arrange
        const long recipientId = 77777L;
        var notification = new Notification("warning", TelegramMessage.Plain("Some message"));

        _mockDmService.SendDmWithEntitiesAsync(
                Arg.Any<UserIdentity>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<MessageEntity>>(),
                Arg.Any<CancellationToken>())
            .Returns(new DmDeliveryResult { DmSent = true });

        // Act
        await _channel.SendAsync(recipientId.ToString(), notification);

        // Assert — no parse_mode anywhere: the channel routes through the entity send with the
        // plain message text and an empty entity list.
        await _mockDmService.Received(1).SendDmWithEntitiesAsync(
            Arg.Any<UserIdentity>(),
            "warning",
            "Some message",
            Arg.Is<IReadOnlyList<MessageEntity>>(e => e!.Count == 0),
            Arg.Any<CancellationToken>());
    }
}
