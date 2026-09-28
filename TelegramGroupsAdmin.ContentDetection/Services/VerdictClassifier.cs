using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// The single place that decides a verdict event's classification. Pure and static: every
/// verdict write goes through DetectionResultsRepository, which calls this.
/// </summary>
public static class VerdictClassifier
{
    public static VerdictClassification ClassifyScan(ContentDetectionResult scan)
    {
        ArgumentNullException.ThrowIfNull(scan);

        if (scan.IsSpam)
        {
            return IsTrainingWorthy(scan)
                ? VerdictClassification.ImplicitSpam
                : VerdictClassification.UntrainedSpam;
        }

        // A positive, non-abstained AI score on a ham verdict means the AI said "review"/"spam"
        // below the review threshold: allowed, but too uncertain to learn ham from.
        var ai = scan.CheckResults.FirstOrDefault(c => c.CheckName == CheckName.OpenAI);
        return ai is { Abstained: false, Score: > 0 }
            ? VerdictClassification.UntrainedHam
            : VerdictClassification.ImplicitHam;
    }

    public static VerdictClassification ClassifyFileScan(bool infected) =>
        infected ? VerdictClassification.UntrainedSpam : VerdictClassification.UntrainedHam;

    public static VerdictClassification ClassifyDecision(VerdictSource source, bool? isSpam = null) => source switch
    {
        VerdictSource.AutoBan or VerdictSource.WebMarkSpam or VerdictSource.SpamCommand or VerdictSource.ReviewSpam
            => VerdictClassification.ExplicitSpam,
        VerdictSource.WebMarkHam => VerdictClassification.ExplicitHam,
        VerdictSource.ReviewDismiss => VerdictClassification.ImplicitHam,
        VerdictSource.TrainingDataPage or VerdictSource.Import or VerdictSource.LegacyManual
            => RequireIsSpam(source, isSpam) ? VerdictClassification.ExplicitSpam : VerdictClassification.ExplicitHam,
        VerdictSource.TrainingExclude
            => RequireIsSpam(source, isSpam) ? VerdictClassification.UntrainedSpam : VerdictClassification.UntrainedHam,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source,
            "Scan sources are classified with ClassifyScan / ClassifyFileScan")
    };

    /// <summary>
    /// A scan is training-worthy when OpenAI was confident, or, with no OpenAI check, when the
    /// total score is very high.
    /// </summary>
    public static bool IsTrainingWorthy(ContentDetectionResult scan)
    {
        var ai = scan.CheckResults.FirstOrDefault(c => c.CheckName == CheckName.OpenAI);
        return ai != null
            ? ai.Score >= ContentDetectionConstants.OpenAIConfidentThreshold
            : scan.TotalScore > ContentDetectionConstants.TrainingConfidenceThreshold;
    }

    private static bool RequireIsSpam(VerdictSource source, bool? isSpam) =>
        isSpam ?? throw new ArgumentException($"{source} requires an explicit isSpam value", nameof(isSpam));
}
