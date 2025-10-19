namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// Report review status for spam and impersonation reports (stored as INT in database)
/// </summary>
public enum ReportStatus
{
    /// <summary>Report awaiting admin review</summary>
    Pending = 0,
    /// <summary>Report reviewed and action taken</summary>
    Reviewed = 1,
    /// <summary>Report reviewed and marked as false positive</summary>
    Dismissed = 2
}
