namespace TelegramGroupsAdmin.Core.Models.BackgroundJobSettings;

/// <summary>
/// Settings for User Photo Refresh job.
/// </summary>
public record UserPhotoRefreshSettings
{
    /// <summary>
    /// Refresh photos for users active within this many days (default: 7).
    /// </summary>
    public int DaysBack { get; init; } = 7;
}
