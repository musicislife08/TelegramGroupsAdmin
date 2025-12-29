using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Services.BackgroundServices;

namespace TelegramGroupsAdmin.Ui.Server.Services;

/// <summary>
/// Background service that bridges events from MessageProcessingService to SSE clients.
/// Subscribes to message processing events and broadcasts them via SSE.
/// </summary>
public class SseEventBridgeService : IHostedService
{
    private readonly IMessageProcessingService _messageProcessingService;
    private readonly SseConnectionManager _sseConnectionManager;
    private readonly ILogger<SseEventBridgeService> _logger;

    public SseEventBridgeService(
        IMessageProcessingService messageProcessingService,
        SseConnectionManager sseConnectionManager,
        ILogger<SseEventBridgeService> logger)
    {
        _messageProcessingService = messageProcessingService;
        _sseConnectionManager = sseConnectionManager;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("SSE Event Bridge starting - subscribing to message events");

        // Subscribe to message processing events
        _messageProcessingService.OnNewMessage += HandleNewMessage;
        _messageProcessingService.OnMessageEdited += HandleMessageEdited;
        _messageProcessingService.OnMediaUpdated += HandleMediaUpdated;

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("SSE Event Bridge stopping - unsubscribing from message events");

        // Unsubscribe from events
        _messageProcessingService.OnNewMessage -= HandleNewMessage;
        _messageProcessingService.OnMessageEdited -= HandleMessageEdited;
        _messageProcessingService.OnMediaUpdated -= HandleMediaUpdated;

        return Task.CompletedTask;
    }

    private void HandleNewMessage(MessageRecord message)
    {
        _logger.LogDebug("Broadcasting message.new event for message {MessageId} in chat {ChatId}",
            message.MessageId, message.ChatId);

        // Fire and forget - don't block the message processing pipeline
        _ = BroadcastEventAsync("message.new", new
        {
            messageId = message.MessageId,
            chatId = message.ChatId,
            userId = message.UserId,
            timestamp = message.Timestamp,
            previewText = FormatMessagePreview(message)
        });
    }

    /// <summary>
    /// Format message text for sidebar preview display.
    /// Truncates long messages and shows media type indicator for media-only messages.
    /// </summary>
    private static string FormatMessagePreview(MessageRecord message)
    {
        const int maxLength = 50;

        if (!string.IsNullOrWhiteSpace(message.MessageText))
        {
            var cleaned = message.MessageText.ReplaceLineEndings(" ").Trim();
            return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength] + "…";
        }

        // No text - check for photo first (stored separately from MediaType)
        if (!string.IsNullOrEmpty(message.PhotoFileId))
            return "📷 Photo";

        // Show media type indicator
        return message.MediaType switch
        {
            MediaType.Animation => "🎬 GIF",
            MediaType.Video => "🎥 Video",
            MediaType.Audio => "🎵 Audio",
            MediaType.Voice => "🎤 Voice message",
            MediaType.VideoNote => "📹 Video message",
            MediaType.Sticker => "🎨 Sticker",
            MediaType.Document => "📎 Document",
            _ => "📎 Attachment"
        };
    }

    private void HandleMessageEdited(MessageEditRecord edit)
    {
        _logger.LogDebug("Broadcasting message.edited event for message {MessageId}",
            edit.MessageId);

        _ = BroadcastEventAsync("message.edited", new
        {
            messageId = edit.MessageId,
            editId = edit.Id,
            timestamp = edit.EditDate
        });
    }

    private void HandleMediaUpdated(long messageId, MediaType mediaType)
    {
        _logger.LogDebug("Broadcasting message.media event for message {MessageId}",
            messageId);

        _ = BroadcastEventAsync("message.media", new
        {
            messageId = messageId,
            mediaType = mediaType.ToString()
        });
    }

    private async Task BroadcastEventAsync(string eventType, object data)
    {
        try
        {
            await _sseConnectionManager.BroadcastAsync(eventType, data);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast SSE event {EventType}", eventType);
        }
    }
}
