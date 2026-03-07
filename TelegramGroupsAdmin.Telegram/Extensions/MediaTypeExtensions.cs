using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Extensions;

public static class MediaTypeExtensions
{
    extension(MediaType mediaType)
    {
        /// <summary>
        /// User-facing display name for a media type (e.g., "GIF", "Voice message").
        /// Used in message bubbles and sidebar previews.
        /// </summary>
        public string ToDisplayName() => mediaType switch
        {
            MediaType.Animation => "GIF",
            MediaType.Video => "Video",
            MediaType.Audio => "Audio",
            MediaType.Voice => "Voice message",
            MediaType.Sticker => "Sticker",
            MediaType.VideoNote => "Video message",
            MediaType.Document => "Document",
            _ => "Media"
        };
    }
}
