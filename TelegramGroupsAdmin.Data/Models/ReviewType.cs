namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// Discriminator for unified reviews table.
/// Stored as smallint in database.
/// </summary>
public enum ReviewType : short
{
    /// <summary>Content report (user /report, web UI, or system OpenAI veto)</summary>
    Report = 0,

    /// <summary>Auto-detected account impersonation</summary>
    ImpersonationAlert = 1,

    /// <summary>Failed entrance exam awaiting admin decision</summary>
    ExamFailure = 2
}
