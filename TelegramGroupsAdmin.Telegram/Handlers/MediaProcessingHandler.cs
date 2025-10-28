using Telegram.Bot.Types;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.Telegram.Handlers;

/// <summary>
/// Handles media detection and processing for Telegram messages.
/// Extracts media metadata and coordinates download via TelegramMediaService.
/// Supports: Animation (GIF), Video, Audio, Voice, Sticker, VideoNote, Document
/// </summary>
public class MediaProcessingHandler
{
    private readonly TelegramMediaService _mediaService;

    public MediaProcessingHandler(TelegramMediaService mediaService)
    {
        _mediaService = mediaService;
    }

    /// <summary>
    /// Process media attachment from message: detect metadata and download (except Documents).
    /// Documents are metadata-only; file scanner handles temporary download for malware scanning.
    /// </summary>
    public async Task<MediaProcessingResult?> ProcessMediaAsync(
        Message message,
        long chatId,
        int messageId,
        CancellationToken cancellationToken = default)
    {
        var detection = DetectMediaAttachment(message);
        if (detection == null)
        {
            return null;
        }

        // Download and save media file (EXCEPT Documents - metadata only for Documents)
        // Document files are only downloaded temporarily by the file scanner for malware detection
        string? localPath = null;
        if (detection.MediaType != MediaType.Document)
        {
            localPath = await _mediaService.DownloadAndSaveMediaAsync(
                detection.FileId,
                detection.MediaType,
                detection.FileName,
                chatId,
                messageId,
                cancellationToken);
        }

        return new MediaProcessingResult(
            MediaType: detection.MediaType,
            FileId: detection.FileId,
            FileSize: detection.FileSize,
            FileName: detection.FileName,
            MimeType: detection.MimeType,
            Duration: detection.Duration,
            LocalPath: localPath
        );
    }

    /// <summary>
    /// Detect media attachment in message and extract metadata.
    /// Returns null if no media attachment found.
    ///
    /// Priority order (important for dual-property messages like GIFs):
    /// Animation → Video → Audio → Voice → Sticker → VideoNote → Document
    /// </summary>
    public static MediaDetectionResult? DetectMediaAttachment(Message message)
    {
        // Animation (GIF)
        if (message.Animation != null)
        {
            return new MediaDetectionResult(
                MediaType: MediaType.Animation,
                FileId: message.Animation.FileId,
                FileSize: message.Animation.FileSize ?? 0,
                FileName: message.Animation.FileName,
                MimeType: message.Animation.MimeType ?? "video/mp4",
                Duration: message.Animation.Duration
            );
        }

        // Video
        if (message.Video != null)
        {
            return new MediaDetectionResult(
                MediaType: MediaType.Video,
                FileId: message.Video.FileId,
                FileSize: message.Video.FileSize ?? 0,
                FileName: message.Video.FileName,
                MimeType: message.Video.MimeType ?? "video/mp4",
                Duration: message.Video.Duration
            );
        }

        // Audio (music files with metadata)
        if (message.Audio != null)
        {
            return new MediaDetectionResult(
                MediaType: MediaType.Audio,
                FileId: message.Audio.FileId,
                FileSize: message.Audio.FileSize ?? 0,
                FileName: message.Audio.FileName ?? message.Audio.Title ?? $"audio_{message.Audio.FileUniqueId}.mp3",
                MimeType: message.Audio.MimeType ?? "audio/mpeg",
                Duration: message.Audio.Duration
            );
        }

        // Voice message (OGG format voice note)
        if (message.Voice != null)
        {
            return new MediaDetectionResult(
                MediaType: MediaType.Voice,
                FileId: message.Voice.FileId,
                FileSize: message.Voice.FileSize ?? 0,
                FileName: $"voice_{message.Voice.FileUniqueId}.ogg",
                MimeType: message.Voice.MimeType ?? "audio/ogg",
                Duration: message.Voice.Duration
            );
        }

        // Sticker (WebP format)
        if (message.Sticker != null)
        {
            return new MediaDetectionResult(
                MediaType: MediaType.Sticker,
                FileId: message.Sticker.FileId,
                FileSize: message.Sticker.FileSize ?? 0,
                FileName: $"sticker_{message.Sticker.FileUniqueId}.webp",
                MimeType: "image/webp",
                Duration: null // Stickers don't have duration
            );
        }

        // Video note (circular video message)
        if (message.VideoNote != null)
        {
            return new MediaDetectionResult(
                MediaType: MediaType.VideoNote,
                FileId: message.VideoNote.FileId,
                FileSize: message.VideoNote.FileSize ?? 0,
                FileName: $"videonote_{message.VideoNote.FileUniqueId}.mp4",
                MimeType: "video/mp4",
                Duration: message.VideoNote.Duration
            );
        }

        // Document attachments: Metadata ONLY (filename, size, MIME type)
        // DON'T download for display - file scanner handles temporary download for malware scanning
        // UI will show document icon with filename but no preview/download link
        if (message.Document != null)
        {
            return new MediaDetectionResult(
                MediaType: MediaType.Document,
                FileId: message.Document.FileId,
                FileSize: message.Document.FileSize ?? 0,
                FileName: message.Document.FileName ?? "document",
                MimeType: message.Document.MimeType,
                Duration: null // Documents don't have duration
            );
        }

        return null;
    }
}

/// <summary>
/// Result of media detection (pure metadata extraction from Telegram message)
/// </summary>
public record MediaDetectionResult(
    MediaType MediaType,
    string FileId,
    long FileSize,
    string? FileName,
    string? MimeType,
    int? Duration
);

/// <summary>
/// Result of complete media processing (detection + download coordination)
/// </summary>
public record MediaProcessingResult(
    MediaType MediaType,
    string FileId,
    long FileSize,
    string? FileName,
    string? MimeType,
    int? Duration,
    string? LocalPath // Null for Documents (metadata-only)
);
