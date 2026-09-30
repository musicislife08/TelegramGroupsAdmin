using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.UnitTests.Core.Models;

/// <summary>
/// Pins the stored numeric values of the verdict enums. Data stores these as int, so a
/// renumbering would silently change the meaning of every existing detection_results row.
/// </summary>
[TestFixture]
public class VerdictTypesTests
{
    [TestCase(VerdictSource.ContentScan, 0)]
    [TestCase(VerdictSource.FileScan, 1)]
    [TestCase(VerdictSource.AutoBan, 10)]
    [TestCase(VerdictSource.WebMarkSpam, 11)]
    [TestCase(VerdictSource.WebMarkHam, 12)]
    [TestCase(VerdictSource.SpamCommand, 13)]
    [TestCase(VerdictSource.ReviewSpam, 14)]
    [TestCase(VerdictSource.TrainingDataPage, 16)]
    [TestCase(VerdictSource.TrainingExclude, 17)]
    [TestCase(VerdictSource.Import, 18)]
    [TestCase(VerdictSource.ReviewClean, 19)]
    [TestCase(VerdictSource.LegacyManual, 99)]
    public void VerdictSource_HasPinnedValue(VerdictSource source, int expected)
        => Assert.That((int)source, Is.EqualTo(expected));

    [Test]
    public void VerdictSource_EveryMemberIsPinned()
        => Assert.That(Enum.GetValues<VerdictSource>(), Has.Length.EqualTo(12));

    [TestCase(VerdictClassification.ExplicitSpam, 0)]
    [TestCase(VerdictClassification.ExplicitHam, 1)]
    [TestCase(VerdictClassification.ImplicitSpam, 2)]
    [TestCase(VerdictClassification.ImplicitHam, 3)]
    [TestCase(VerdictClassification.UntrainedSpam, 4)]
    [TestCase(VerdictClassification.UntrainedHam, 5)]
    [TestCase(VerdictClassification.Unscanned, 6)]
    public void VerdictClassification_HasPinnedValue(VerdictClassification classification, int expected)
        => Assert.That((int)classification, Is.EqualTo(expected));

    [Test]
    public void VerdictClassification_EveryMemberIsPinned()
        => Assert.That(Enum.GetValues<VerdictClassification>(), Has.Length.EqualTo(7));

    [TestCase(VerdictClassification.ExplicitSpam, true)]
    [TestCase(VerdictClassification.ImplicitSpam, true)]
    [TestCase(VerdictClassification.UntrainedSpam, true)]
    [TestCase(VerdictClassification.ExplicitHam, false)]
    [TestCase(VerdictClassification.ImplicitHam, false)]
    [TestCase(VerdictClassification.UntrainedHam, false)]
    [TestCase(VerdictClassification.Unscanned, false)]
    public void IsSpam_MatchesSpamRow(VerdictClassification classification, bool expected)
        => Assert.That(classification.IsSpam(), Is.EqualTo(expected));

    [Test]
    public void IntSets_MirrorEnumSets()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(VerdictClassifications.SpamValues, Is.EquivalentTo(VerdictClassifications.Spam.Select(c => (int)c)));
            Assert.That(VerdictClassifications.TrainingSpamValues, Is.EquivalentTo(VerdictClassifications.TrainingSpam.Select(c => (int)c)));
            Assert.That(VerdictClassifications.CuratedValues, Is.EquivalentTo(VerdictClassifications.Curated.Select(c => (int)c)));
        }
    }

    [Test]
    public void TrainingSets_AreDisjointAndExcludeUntrained()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(VerdictClassifications.TrainingSpam, Is.EquivalentTo(new[] { VerdictClassification.ExplicitSpam, VerdictClassification.ImplicitSpam }));
            Assert.That(VerdictClassifications.Curated, Is.EquivalentTo(new[] { VerdictClassification.ExplicitSpam, VerdictClassification.ExplicitHam, VerdictClassification.ImplicitSpam }));
        }
    }

    [Test]
    public void VerdictSourceToDisplayText_NamesEverySourceWithoutJargon()
    {
        using (Assert.EnterMultipleScope())
        {
            foreach (var source in Enum.GetValues<VerdictSource>())
            {
                var text = source.ToDisplayText();
                Assert.That(text, Is.Not.EqualTo(source.ToString()), $"{source} has no display text");
                Assert.That(text, Does.Not.Contain("ham").IgnoreCase, $"{source} display text uses 'ham'");
            }
        }
    }

    [TestCase(VerdictSource.WebMarkHam, "Marked clean (web)")]
    [TestCase(VerdictSource.ReviewClean, "Review queue: clean")]
    [TestCase(VerdictSource.ContentScan, "Automatic scan")]
    public void VerdictSourceToDisplayText_Examples(VerdictSource source, string expected)
        => Assert.That(source.ToDisplayText(), Is.EqualTo(expected));

    [Test]
    public void ClassificationSets_AreReadOnly()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(VerdictClassifications.Spam, Is.InstanceOf<System.Collections.ObjectModel.ReadOnlyCollection<VerdictClassification>>());
            Assert.That(VerdictClassifications.TrainingSpam, Is.InstanceOf<System.Collections.ObjectModel.ReadOnlyCollection<VerdictClassification>>());
            Assert.That(VerdictClassifications.Curated, Is.InstanceOf<System.Collections.ObjectModel.ReadOnlyCollection<VerdictClassification>>());
        }
    }

    [TestCase(VerdictClassification.ExplicitSpam, true)]
    [TestCase(VerdictClassification.ExplicitHam, true)]
    [TestCase(VerdictClassification.ImplicitSpam, true)]
    [TestCase(VerdictClassification.ImplicitHam, true)]
    [TestCase(VerdictClassification.UntrainedSpam, false)]
    [TestCase(VerdictClassification.UntrainedHam, false)]
    [TestCase(VerdictClassification.Unscanned, false)]
    public void IsTrainingSample_OnlyTrainedClassifications(VerdictClassification c, bool expected)
        => Assert.That(c.IsTrainingSample(), Is.EqualTo(expected));

    [TestCase(VerdictClassification.ExplicitSpam, "Spam (confirmed)")]
    [TestCase(VerdictClassification.ExplicitHam, "Clean (confirmed)")]
    [TestCase(VerdictClassification.ImplicitSpam, "Spam (auto)")]
    [TestCase(VerdictClassification.ImplicitHam, "Clean (auto)")]
    [TestCase(VerdictClassification.UntrainedSpam, "Spam (not trained)")]
    [TestCase(VerdictClassification.UntrainedHam, "Clean (not trained)")]
    [TestCase(VerdictClassification.Unscanned, "Not scanned")]
    public void ToDisplayText_MatchesEveryValue(VerdictClassification c, string expected)
        => Assert.That(c.ToDisplayText(), Is.EqualTo(expected));

    [Test]
    public void IntValueSets_AreReadOnly()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(VerdictClassifications.SpamValues, Is.InstanceOf<System.Collections.ObjectModel.ReadOnlyCollection<int>>());
            Assert.That(VerdictClassifications.TrainingSpamValues, Is.InstanceOf<System.Collections.ObjectModel.ReadOnlyCollection<int>>());
            Assert.That(VerdictClassifications.CuratedValues, Is.InstanceOf<System.Collections.ObjectModel.ReadOnlyCollection<int>>());
        }
    }
}
