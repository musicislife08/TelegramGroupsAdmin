using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.Models.Analytics;

/// <summary>
/// Top active user for leaderboard
/// </summary>
public class TopActiveUser
{
    public long TelegramUserId { get; set; }
    public string? Username { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? UserPhotoPath { get; set; }
    public int MessageCount { get; set; }

    /// <summary>
    /// Percentage of total messages in the selected period (UX-2.1)
    /// </summary>
    public double Percentage { get; set; }

    public string DisplayName => TelegramDisplayName.Format(FirstName, LastName, Username, TelegramUserId);
}
