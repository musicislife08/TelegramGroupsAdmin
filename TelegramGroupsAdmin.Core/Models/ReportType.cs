namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// Type of report in the unified reports queue (domain enum).
/// Repository maps this to/from short in Data layer.
/// </summary>
public enum ReportType
{
    /// <summary>Content report (user /report, web UI, or system OpenAI veto)</summary>
    ContentReport = 0,

    /// <summary>Auto-detected account impersonation</summary>
    ImpersonationAlert = 1,

    /// <summary>Failed entrance exam awaiting admin decision</summary>
    ExamFailure = 2
}
