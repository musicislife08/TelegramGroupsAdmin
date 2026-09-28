namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// Fixed groupings of <see cref="VerdictClassification"/>. The int twins exist for EF LINQ
/// over Data DTOs, which store the classification as int.
/// The database's generated is_spam column uses the same Spam set (pinned by a parity test).
/// </summary>
public static class VerdictClassifications
{
    public static readonly VerdictClassification[] Spam =
        [VerdictClassification.ExplicitSpam, VerdictClassification.ImplicitSpam, VerdictClassification.UntrainedSpam];

    public static readonly VerdictClassification[] TrainingSpam =
        [VerdictClassification.ExplicitSpam, VerdictClassification.ImplicitSpam];

    public static readonly VerdictClassification[] TrainingHam =
        [VerdictClassification.ExplicitHam, VerdictClassification.ImplicitHam, VerdictClassification.Unscanned];

    /// <summary>
    /// The curated training set: explicit labels plus confident implicit spam. Retention keeps
    /// these messages, and the Training Data page lists them. (Implicit ham is too plentiful for either.)
    /// </summary>
    public static readonly VerdictClassification[] Curated =
        [VerdictClassification.ExplicitSpam, VerdictClassification.ExplicitHam, VerdictClassification.ImplicitSpam];

    public static readonly int[] SpamValues = [.. Spam.Select(c => (int)c)];
    public static readonly int[] TrainingSpamValues = [.. TrainingSpam.Select(c => (int)c)];
    public static readonly int[] TrainingHamValues = [.. TrainingHam.Select(c => (int)c)];
    public static readonly int[] CuratedValues = [.. Curated.Select(c => (int)c)];

    extension(VerdictClassification classification)
    {
        public bool IsSpam() => Spam.Contains(classification);

        public bool IsExplicit() =>
            classification is VerdictClassification.ExplicitSpam or VerdictClassification.ExplicitHam;
    }
}
