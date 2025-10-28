namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Recommended moderation actions based on spam detection confidence
/// </summary>
public enum SpamAction
{
    /// <summary>Allow the message to remain (low confidence spam)</summary>
    Allow = 0,

    /// <summary>Flag for admin review (medium confidence spam)</summary>
    ReviewQueue = 1,

    /// <summary>Auto-ban user and delete message (high confidence spam)</summary>
    AutoBan = 2
}
