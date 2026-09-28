namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// Fixed groupings of <see cref="VerdictClassification"/>. The int twins exist for EF LINQ
/// over Data DTOs, which store the classification as int.
/// The database's generated is_spam column uses the same Spam set (pinned by a parity test).
/// </summary>
public static class VerdictClassifications
{
    public static readonly IReadOnlyList<VerdictClassification> Spam =
        Array.AsReadOnly([VerdictClassification.ExplicitSpam, VerdictClassification.ImplicitSpam, VerdictClassification.UntrainedSpam]);

    public static readonly IReadOnlyList<VerdictClassification> TrainingSpam =
        Array.AsReadOnly([VerdictClassification.ExplicitSpam, VerdictClassification.ImplicitSpam]);

    public static readonly IReadOnlyList<VerdictClassification> TrainingHam =
        Array.AsReadOnly([VerdictClassification.ExplicitHam, VerdictClassification.ImplicitHam, VerdictClassification.Unscanned]);

    /// <summary>
    /// The curated training set: explicit labels plus confident implicit spam. Retention keeps
    /// these messages, and the Training Data page lists them. (Implicit ham is too plentiful for either.)
    /// </summary>
    public static readonly IReadOnlyList<VerdictClassification> Curated =
        Array.AsReadOnly([VerdictClassification.ExplicitSpam, VerdictClassification.ExplicitHam, VerdictClassification.ImplicitSpam]);

    public static readonly IReadOnlyList<int> SpamValues = Array.AsReadOnly(Spam.Select(c => (int)c).ToArray());
    public static readonly IReadOnlyList<int> TrainingSpamValues = Array.AsReadOnly(TrainingSpam.Select(c => (int)c).ToArray());
    public static readonly IReadOnlyList<int> TrainingHamValues = Array.AsReadOnly(TrainingHam.Select(c => (int)c).ToArray());
    public static readonly IReadOnlyList<int> CuratedValues = Array.AsReadOnly(Curated.Select(c => (int)c).ToArray());

    extension(VerdictClassification classification)
    {
        public bool IsSpam() => Spam.Contains(classification);

        public bool IsExplicit() =>
            classification is VerdictClassification.ExplicitSpam or VerdictClassification.ExplicitHam;

        /// <summary>True when the verdict trains the classifiers (explicit or implicit, spam or ham).</summary>
        public bool IsTrainingSample() => classification is VerdictClassification.ExplicitSpam or VerdictClassification.ExplicitHam
            or VerdictClassification.ImplicitSpam or VerdictClassification.ImplicitHam;

        public string ToDisplayText() => classification switch
        {
            VerdictClassification.ExplicitSpam => "Spam (confirmed)",
            VerdictClassification.ExplicitHam => "Clean (confirmed)",
            VerdictClassification.ImplicitSpam => "Spam (auto)",
            VerdictClassification.ImplicitHam => "Clean (auto)",
            VerdictClassification.UntrainedSpam => "Spam (not trained)",
            VerdictClassification.UntrainedHam => "Clean (not trained)",
            _ => "Not scanned"
        };
    }
}
