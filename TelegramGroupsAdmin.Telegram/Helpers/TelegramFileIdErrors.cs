namespace TelegramGroupsAdmin.Telegram.Helpers;

/// <summary>
/// Recognises Telegram errors meaning a cached file_id is no longer usable, so callers
/// can clear the cache and fall back to uploading from disk.
/// </summary>
public static class TelegramFileIdErrors
{
    public static bool IsInvalidFileId(Exception ex)
    {
        var message = ex.Message.ToLowerInvariant();
        return message.Contains("wrong file identifier") ||
               message.Contains("file_id") ||
               message.Contains("invalid file");
    }
}
