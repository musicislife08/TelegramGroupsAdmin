namespace TelegramGroupsAdmin.Telegram.Abstractions;

/// <summary>
/// Payload for RefreshUserPhotosJob - nightly refresh of active user photos
/// </summary>
public record RefreshUserPhotosPayload
{
    /// <summary>
    /// Number of days to look back for "active" users
    /// Default: 30 days
    /// </summary>
    public int DaysBack { get; init; } = 30;
}
