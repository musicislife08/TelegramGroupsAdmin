using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.UnitTests.Data;

/// <summary>
/// Pins the Data layer's SQL IN-list fragments to Core's verdict enums (Data cannot reference Core).
/// </summary>
[TestFixture]
public class VerdictSqlTests
{
    private static string InList(params VerdictSource[] sources) => string.Join(", ", sources.Select(s => (int)s));

    [Test]
    public void SpamClassifications_MatchesCoreSpamSet()
        => Assert.That(string.Join(", ", VerdictClassifications.SpamValues), Is.EqualTo(VerdictSql.SpamClassifications));

    [Test]
    public void CorrectionSources_AreTheHumanDecisionsThatCanContradictAScan()
        => Assert.That(InList(
            VerdictSource.WebMarkSpam, VerdictSource.WebMarkHam, VerdictSource.SpamCommand,
            VerdictSource.ReviewSpam, VerdictSource.ReviewClean, VerdictSource.LegacyManual),
            Is.EqualTo(VerdictSql.CorrectionSources));

    [Test]
    public void ManualSources_AreEveryHumanDecision()
        => Assert.That(InList(
            VerdictSource.WebMarkSpam, VerdictSource.WebMarkHam, VerdictSource.SpamCommand,
            VerdictSource.ReviewSpam, VerdictSource.TrainingDataPage, VerdictSource.TrainingExclude,
            VerdictSource.ReviewClean, VerdictSource.LegacyManual),
            Is.EqualTo(VerdictSql.ManualSources));
}
