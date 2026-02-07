namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Message metadata from Telegram, based on tg-spam's MetaData model
/// </summary>
public record ContentCheckMetadata
{
    /// <summary>
    /// Number of images in message
    /// </summary>
    public int Images { get; init; } = 0;

    /// <summary>
    /// Number of links in message (HTTP/HTTPS URLs)
    /// </summary>
    public int Links { get; init; } = 0;

    /// <summary>
    /// Number of @username mentions in message
    /// </summary>
    public int Mentions { get; init; } = 0;

    /// <summary>
    /// Message contains video
    /// </summary>
    public bool HasVideo { get; init; } = false;

    /// <summary>
    /// Message contains audio
    /// </summary>
    public bool HasAudio { get; init; } = false;

    /// <summary>
    /// Message is forwarded from another chat
    /// </summary>
    public bool HasForward { get; init; } = false;

    /// <summary>
    /// Message contains inline keyboard (buttons)
    /// </summary>
    public bool HasKeyboard { get; init; } = false;

    /// <summary>
    /// Message is a reply to a channel post (linked channel or anonymous admin posting as group)
    /// </summary>
    public bool IsReplyToChannelPost { get; init; }

    /// <summary>
    /// Telegram message ID for duplicate tracking
    /// </summary>
    public int MessageId { get; init; } = 0;
}
