using Microsoft.EntityFrameworkCore;
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
        // R18 (Task 8 fix round): asserting by MessageId is not reliable here. Canonical
        // message 213409 (this anchor) and message 105678 (Workshop Alumni) share
        // byte-identical scrubbed placeholder text ("Lorem ipsum dolor sit amet,
        // consectetur adipiscing" — same content_hash/similarity_hash), and both resolve
        // to ExplicitHam under the view. DeduplicateSamples (SimHash, untouched by this
        // test) correctly keeps only one representative of that near-duplicate pair, and
        // which MessageId survives is not guaranteed to be 213409's. Deduplicating
        // identical texts is correct behavior — the plan defect was asserting per-MessageId
        // against two colliding canonical rows. Assert by TEXT instead: the repository
        // must never classify that text as spam, and must keep exactly one Explicit ham
        // sample carrying it.
        var text = await GetMessageTextAsync(GoldenDatasetConstants.Chats.MainChatId, GoldenDatasetConstants.Verdicts.CorrectedToHamMsgId);

        var (spam, ham) = await LoadAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(spam.Any(s => s.Text == text), Is.False);
            Assert.That(ham.Single(s => s.Text == text).Source, Is.EqualTo(TrainingSampleSource.Explicit));
        }
    }

    /// <summary>
    /// Mirrors MLTrainingDataRepository's text selection: translated text (latest, non-edit)
    /// when one exists, otherwise the message's raw text — the same string the repository
    /// would have put into a TrainingSample.
    /// </summary>
    private async Task<string> GetMessageTextAsync(long chatId, int messageId)
    {
        await using var ctx = _helper.GetDbContext();
        var message = await ctx.Messages.SingleAsync(m => m.ChatId == chatId && m.MessageId == messageId);
        var translation = await ctx.MessageTranslations
            .Where(t => t.MessageId == messageId && t.ChatId == chatId && t.EditId == null)
            .OrderByDescending(t => t.TranslatedAt)
            .FirstOrDefaultAsync();
        return translation?.TranslatedText ?? message.MessageText!;
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

