namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Result of user photo fetch operation with metadata
/// </summary>
public record UserPhotoResult(string RelativePath, string FileUniqueId);
