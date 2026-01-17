namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Type of report in the unified reports queue
/// </summary>
public enum ReportType
{
    /// <summary>Spam or suspicious content report</summary>
    Spam = 0,

    /// <summary>Potential user impersonation alert</summary>
    Impersonation = 1,

    /// <summary>Failed entrance exam pending review</summary>
    ExamFailure = 2
}
