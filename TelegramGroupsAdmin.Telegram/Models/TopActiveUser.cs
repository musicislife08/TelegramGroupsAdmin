using TelegramGroupsAdmin.Core;

namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Top active user for leaderboard
/// </summary>
public class TopActiveUser
{
    public long TelegramUserId { get; set; }
    public string? Username { get; set; }
    public string? FirstName { get; set; }
    public string? UserPhotoPath { get; set; }
    public int MessageCount { get; set; }

    /// <summary>
    /// Percentage of total messages in the selected period (UX-2.1)
    /// </summary>
    public double Percentage { get; set; }

    public string DisplayName
    {
        get
        {
            // Special handling for Telegram service account (channel posts, anonymous admin posts)
            if (TelegramUserId == TelegramConstants.ServiceAccountUserId)
            {
                return "Telegram Service Account";
            }

            return !string.IsNullOrEmpty(Username) ? $"@{Username}" : FirstName ?? $"User {TelegramUserId}";
        }
    }
}
