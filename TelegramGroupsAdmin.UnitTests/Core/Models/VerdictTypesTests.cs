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
    [TestCase(VerdictSource.ReviewDismiss, 15)]
    [TestCase(VerdictSource.TrainingDataPage, 16)]
    [TestCase(VerdictSource.TrainingExclude, 17)]
    [TestCase(VerdictSource.Import, 18)]
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
            Assert.That(VerdictClassifications.TrainingHamValues, Is.EquivalentTo(VerdictClassifications.TrainingHam.Select(c => (int)c)));
            Assert.That(VerdictClassifications.CuratedValues, Is.EquivalentTo(VerdictClassifications.Curated.Select(c => (int)c)));
        }
    }

    [Test]
    public void TrainingSets_AreDisjointAndExcludeUntrained()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(VerdictClassifications.TrainingSpam, Is.EquivalentTo(new[] { VerdictClassification.ExplicitSpam, VerdictClassification.ImplicitSpam }));
            Assert.That(VerdictClassifications.TrainingHam, Is.EquivalentTo(new[] { VerdictClassification.ExplicitHam, VerdictClassification.ImplicitHam, VerdictClassification.Unscanned }));
            Assert.That(VerdictClassifications.Curated, Is.EquivalentTo(new[] { VerdictClassification.ExplicitSpam, VerdictClassification.ExplicitHam, VerdictClassification.ImplicitSpam }));
        }
    }

    [TestCase(VerdictSource.ContentScan, true)]
    [TestCase(VerdictSource.FileScan, true)]
    [TestCase(VerdictSource.AutoBan, false)]
    [TestCase(VerdictSource.TrainingExclude, false)]
    [TestCase(VerdictSource.LegacyManual, false)]
    public void IsScan_OnlyForScanSources(VerdictSource source, bool expected)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(source.IsScan(), Is.EqualTo(expected));
            Assert.That(source.IsDecision(), Is.EqualTo(!expected));
        }
    }

    [Test]
    public void ClassificationSets_AreReadOnly()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(VerdictClassifications.Spam, Is.InstanceOf<System.Collections.ObjectModel.ReadOnlyCollection<VerdictClassification>>());
            Assert.That(VerdictClassifications.TrainingSpam, Is.InstanceOf<System.Collections.ObjectModel.ReadOnlyCollection<VerdictClassification>>());
            Assert.That(VerdictClassifications.TrainingHam, Is.InstanceOf<System.Collections.ObjectModel.ReadOnlyCollection<VerdictClassification>>());
            Assert.That(VerdictClassifications.Curated, Is.InstanceOf<System.Collections.ObjectModel.ReadOnlyCollection<VerdictClassification>>());
        }
    }

    [Test]
    public void IntValueSets_AreReadOnly()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(VerdictClassifications.SpamValues, Is.InstanceOf<System.Collections.ObjectModel.ReadOnlyCollection<int>>());
            Assert.That(VerdictClassifications.TrainingSpamValues, Is.InstanceOf<System.Collections.ObjectModel.ReadOnlyCollection<int>>());
            Assert.That(VerdictClassifications.TrainingHamValues, Is.InstanceOf<System.Collections.ObjectModel.ReadOnlyCollection<int>>());
            Assert.That(VerdictClassifications.CuratedValues, Is.InstanceOf<System.Collections.ObjectModel.ReadOnlyCollection<int>>());
        }
    }
}
