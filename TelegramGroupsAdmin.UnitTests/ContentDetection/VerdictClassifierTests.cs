using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.UnitTests.ContentDetection;

[TestFixture]
public class VerdictClassifierTests
{
    private static ContentCheckResponseV2 Check(CheckName name, double score, bool abstained = false) =>
        new() { CheckName = name, Score = score, Abstained = abstained, Details = name.ToString() };

    // Spam scans default to AutoBan (acted on alone); review-queued scans are built with ReviewQueueScan.
    private static ContentDetectionResult Scan(bool isSpam, double total, params ContentCheckResponseV2[] checks) =>
        new()
        {
            IsSpam = isSpam, TotalScore = total, CheckResults = [.. checks],
            RecommendedAction = isSpam ? DetectionAction.AutoBan : DetectionAction.Allow
        };

    private static ContentDetectionResult ReviewQueueScan(double total, params ContentCheckResponseV2[] checks) =>
        Scan(true, total, checks) with { RecommendedAction = DetectionAction.ReviewQueue };

    [Test]
    public void ClassifyScan_ReviewQueuedWithConfidentAI_IsUntrainedSpam()
        => Assert.That(VerdictClassifier.ClassifyScan(ReviewQueueScan(4.5, Check(CheckName.StopWords, 3), Check(CheckName.OpenAI, 4.5))),
            Is.EqualTo(VerdictClassification.UntrainedSpam));

    [Test]
    public void ClassifyScan_ReviewQueuedPipelineAboveTrainingThreshold_IsUntrainedSpam()
        => Assert.That(VerdictClassifier.ClassifyScan(ReviewQueueScan(4.5, Check(CheckName.Bayes, 4.5))),
            Is.EqualTo(VerdictClassification.UntrainedSpam));

    [Test]
    public void ClassifyScan_SpamWithConfidentAI_IsImplicitSpam()
        => Assert.That(VerdictClassifier.ClassifyScan(Scan(true, 4.5, Check(CheckName.StopWords, 3), Check(CheckName.OpenAI, 4.5))),
            Is.EqualTo(VerdictClassification.ImplicitSpam));

    [Test]
    public void ClassifyScan_SpamWithUnconfidentAI_IsUntrainedSpam()
        => Assert.That(VerdictClassifier.ClassifyScan(Scan(true, 3.0, Check(CheckName.StopWords, 3), Check(CheckName.OpenAI, 3.0))),
            Is.EqualTo(VerdictClassification.UntrainedSpam));

    [Test]
    public void ClassifyScan_PipelineSpamAboveTrainingThreshold_IsImplicitSpam()
        => Assert.That(VerdictClassifier.ClassifyScan(Scan(true, 4.1, Check(CheckName.Bayes, 4.1))),
            Is.EqualTo(VerdictClassification.ImplicitSpam));

    [Test]
    public void ClassifyScan_PipelineSpamAtTrainingThreshold_IsUntrainedSpam()
        => Assert.That(VerdictClassifier.ClassifyScan(Scan(true, 4.0, Check(CheckName.Bayes, 4.0))),
            Is.EqualTo(VerdictClassification.UntrainedSpam));

    [Test]
    public void ClassifyScan_HardBlock_IsImplicitSpam()
        => Assert.That(VerdictClassifier.ClassifyScan(Scan(true, ContentDetectionConstants.MaxScore, Check(CheckName.UrlBlocklist, ContentDetectionConstants.MaxScore))),
            Is.EqualTo(VerdictClassification.ImplicitSpam));

    [Test]
    public void ClassifyScan_AIReviewBelowThreshold_IsUntrainedHam()
        => Assert.That(VerdictClassifier.ClassifyScan(Scan(false, 2.3, Check(CheckName.Similarity, 3.5), Check(CheckName.OpenAI, 2.3))),
            Is.EqualTo(VerdictClassification.UntrainedHam));

    [Test]
    public void ClassifyScan_AIVeto_IsImplicitHam()
        => Assert.That(VerdictClassifier.ClassifyScan(Scan(false, 0, Check(CheckName.StopWords, 3), Check(CheckName.OpenAI, 0))),
            Is.EqualTo(VerdictClassification.ImplicitHam));

    [Test]
    public void ClassifyScan_AIAbstainedAndPipelineHam_IsImplicitHam()
        => Assert.That(VerdictClassifier.ClassifyScan(Scan(false, 0.8, Check(CheckName.ChannelReply, 0.8), Check(CheckName.OpenAI, 0, abstained: true))),
            Is.EqualTo(VerdictClassification.ImplicitHam));

    [Test]
    public void ClassifyScan_PipelineOnlyLowScore_IsImplicitHam()
        => Assert.That(VerdictClassifier.ClassifyScan(Scan(false, 0.8, Check(CheckName.ChannelReply, 0.8))),
            Is.EqualTo(VerdictClassification.ImplicitHam));

    [TestCase(true, VerdictClassification.UntrainedSpam)]
    [TestCase(false, VerdictClassification.UntrainedHam)]
    public void ClassifyFileScan_IsAlwaysUntrained(bool infected, VerdictClassification expected)
        => Assert.That(VerdictClassifier.ClassifyFileScan(infected), Is.EqualTo(expected));

    [TestCase(VerdictSource.AutoBan, VerdictClassification.ExplicitSpam)]
    [TestCase(VerdictSource.WebMarkSpam, VerdictClassification.ExplicitSpam)]
    [TestCase(VerdictSource.SpamCommand, VerdictClassification.ExplicitSpam)]
    [TestCase(VerdictSource.ReviewSpam, VerdictClassification.ExplicitSpam)]
    [TestCase(VerdictSource.WebMarkHam, VerdictClassification.ExplicitHam)]
    [TestCase(VerdictSource.ReviewClean, VerdictClassification.ExplicitHam)]
    public void ClassifyDecision_FixedSources(VerdictSource source, VerdictClassification expected)
        => Assert.That(VerdictClassifier.ClassifyDecision(source), Is.EqualTo(expected));

    [TestCase(VerdictSource.TrainingDataPage, true, VerdictClassification.ExplicitSpam)]
    [TestCase(VerdictSource.TrainingDataPage, false, VerdictClassification.ExplicitHam)]
    [TestCase(VerdictSource.Import, true, VerdictClassification.ExplicitSpam)]
    [TestCase(VerdictSource.LegacyManual, false, VerdictClassification.ExplicitHam)]
    [TestCase(VerdictSource.TrainingExclude, true, VerdictClassification.UntrainedSpam)]
    [TestCase(VerdictSource.TrainingExclude, false, VerdictClassification.UntrainedHam)]
    public void ClassifyDecision_ArgumentSources(VerdictSource source, bool isSpam, VerdictClassification expected)
        => Assert.That(VerdictClassifier.ClassifyDecision(source, isSpam), Is.EqualTo(expected));

    [TestCase(VerdictSource.TrainingDataPage)]
    [TestCase(VerdictSource.Import)]
    [TestCase(VerdictSource.LegacyManual)]
    [TestCase(VerdictSource.TrainingExclude)]
    public void ClassifyDecision_ArgumentSourceWithoutIsSpam_Throws(VerdictSource source)
        => Assert.Throws<ArgumentException>(() => VerdictClassifier.ClassifyDecision(source));

    [TestCase(VerdictSource.ContentScan)]
    [TestCase(VerdictSource.FileScan)]
    public void ClassifyDecision_ScanSource_Throws(VerdictSource source)
        => Assert.Throws<ArgumentOutOfRangeException>(() => VerdictClassifier.ClassifyDecision(source, true));

    /// <summary>
    /// Every VerdictSource is either a scan (classified by ClassifyScan / ClassifyFileScan) or a decision
    /// ClassifyDecision handles; a new source that is neither fails here instead of at runtime.
    /// </summary>
    [TestCaseSource(nameof(AllSources))]
    public void ClassifyDecision_EverySource_IsAScanOrClassified(VerdictSource source)
    {
        if (source is VerdictSource.ContentScan or VerdictSource.FileScan)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => VerdictClassifier.ClassifyDecision(source, true));
            return;
        }

        using (Assert.EnterMultipleScope())
        {
            foreach (var isSpam in new[] { true, false })
            {
                var classification = VerdictClassifier.ClassifyDecision(source, isSpam);
                Assert.That(Enum.IsDefined(classification), Is.True, $"{source} isSpam={isSpam}");
                Assert.That(classification, Is.Not.EqualTo(VerdictClassification.Unscanned), $"{source} isSpam={isSpam}");
            }
        }
    }

    private static IEnumerable<VerdictSource> AllSources() => Enum.GetValues<VerdictSource>();
}
