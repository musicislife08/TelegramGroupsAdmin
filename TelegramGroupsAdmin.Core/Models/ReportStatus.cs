namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// Report review status (domain enum).
/// Repository maps this to/from int in Data layer.
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
