namespace TelegramGroupsAdmin.Telegram.Handlers;

/// <summary>
/// Result of image processing (detection + download + thumbnail generation)
/// </summary>
public record ImageProcessingResult(
    string FileId,
    int? FileSize,
    string FullPath,       // Relative path: "full/{chatId}/{messageId}.jpg"
    string ThumbnailPath   // Relative path: "thumbs/{chatId}/{messageId}.jpg"
);
