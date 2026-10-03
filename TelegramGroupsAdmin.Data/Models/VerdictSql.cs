namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// SQL IN-list fragments for verdict sources and classifications, shared by the live view
/// definitions and the generated <c>is_spam</c> column. Data cannot reference Core's enums, so these
/// mirror <c>VerdictClassification</c> / <c>VerdictSource</c> values; a unit test pins them to Core.
/// Migrations keep their own frozen literals and never read these.
/// </summary>
public static class VerdictSql
{
    /// <summary>Spam classifications: ExplicitSpam, ImplicitSpam, UntrainedSpam.</summary>
    public const string SpamClassifications = "0, 2, 4";

    /// <summary>
    /// Human decisions that can contradict a scan: WebMarkSpam, WebMarkHam, SpamCommand, ReviewSpam,
    /// ReviewClean, LegacyManual. An auto-ban agrees with its scan, and training-data edits are not
    /// verdicts on the scan, so neither is a correction.
    /// </summary>
    public const string CorrectionSources = "11, 12, 13, 14, 19, 99";

    /// <summary>
    /// Every human decision: the correction sources plus the training-data page (TrainingDataPage,
    /// TrainingExclude).
    /// </summary>
    public const string ManualSources = "11, 12, 13, 14, 16, 17, 19, 99";
}
