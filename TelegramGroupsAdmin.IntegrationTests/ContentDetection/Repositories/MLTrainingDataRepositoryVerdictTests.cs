using Microsoft.Extensions.Logging.Abstractions;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.IntegrationTests.TestData;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.ContentDetection.Repositories;

/// <summary>
/// The four training levels come from message_verdicts: the latest event wins, and Untrained* never trains.
/// Anchors: GoldenDatasetConstants.Verdicts (canonical edit 2026-09-27).
/// </summary>
[TestFixture]
public class MLTrainingDataRepositoryVerdictTests
{
    private MigrationTestHelper _helper = null!;

    [SetUp]
    public async Task SetUp()
    {
        _helper = new MigrationTestHelper();
        await _helper.CreateDatabaseFromGoldenTemplateAsync();
    }

    [TearDown]
    public void TearDown() => _helper.Dispose();

    private async Task<(List<TrainingSample> Spam, List<TrainingSample> Ham)> LoadAsync()
    {
        await using var ctx = _helper.GetDbContext();
        var repo = new MLTrainingDataRepository(ctx, new SimHashService(), NullLogger<MLTrainingDataRepository>.Instance);
        var spam = await repo.GetSpamSamplesAsync();
        var ham = await repo.GetHamSamplesAsync(spamCount: 10_000); // cap high enough to include every ham candidate
        return (spam, ham);
    }

    [Test]
    public async Task CorrectedToHam_IsExplicitHam_NotSpam()
    {
        var (spam, ham) = await LoadAsync();
        var id = GoldenDatasetConstants.Verdicts.CorrectedToHamMsgId;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(spam.Any(s => s.MessageId == id), Is.False);
            Assert.That(ham.Single(s => s.MessageId == id).Source, Is.EqualTo(TrainingSampleSource.Explicit));
        }
    }

    [Test]
    public async Task EditFlippedToSpam_IsImplicitSpam_NotHam()
    {
        var (spam, ham) = await LoadAsync();
        var id = GoldenDatasetConstants.Verdicts.EditFlipMsgId;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(spam.Single(s => s.MessageId == id).Source, Is.EqualTo(TrainingSampleSource.Implicit));
            Assert.That(ham.Any(s => s.MessageId == id), Is.False);
        }
    }

    [TestCase(GoldenDatasetConstants.Verdicts.SpamInTrustWindowMsgId, TestName = "UntrainedSpam never trains")]
    [TestCase(GoldenDatasetConstants.Verdicts.UntrainedHamMsgId, TestName = "UntrainedHam never trains")]
    public async Task UntrainedVerdicts_AreNotSamples(int messageId)
    {
        var (spam, ham) = await LoadAsync();
        Assert.That(spam.Concat(ham).Any(s => s.MessageId == messageId), Is.False);
    }
}

