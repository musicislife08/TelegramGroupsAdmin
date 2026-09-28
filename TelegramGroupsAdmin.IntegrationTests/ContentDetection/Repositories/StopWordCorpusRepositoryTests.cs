using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestData;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.ContentDetection.Repositories;

[TestFixture]
public class StopWordCorpusRepositoryTests
{
    private MigrationTestHelper _helper = null!;
    private ServiceProvider _provider = null!;
    private IStopWordCorpusRepository _repository = null!;
    private static readonly DateTimeOffset Always = DateTimeOffset.MinValue;

    [SetUp]
    public async Task SetUp()
    {
        _helper = new MigrationTestHelper();
        await _helper.CreateDatabaseFromGoldenTemplateAsync();
        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(o => o.UseNpgsql(_helper.ConnectionString));
        services.AddScoped<IStopWordCorpusRepository, StopWordCorpusRepository>();
        _provider = services.BuildServiceProvider();
        _repository = _provider.GetRequiredService<IStopWordCorpusRepository>();
    }

    [TearDown]
    public void TearDown()
    {
        _provider.Dispose();
        _helper.Dispose();
    }

    [Test]
    public async Task Corpora_FollowTheCurrentVerdict()
    {
        await using var ctx = _helper.GetDbContext();
        var correctedMessage = await ctx.Messages.SingleAsync(m => m.MessageId == GoldenDatasetConstants.Verdicts.CorrectedToHamMsgId
            && m.ChatId == GoldenDatasetConstants.Chats.MainChatId);
        var translation = await ctx.MessageTranslations.SingleOrDefaultAsync(t =>
            t.MessageId == correctedMessage.MessageId && t.ChatId == correctedMessage.ChatId && t.EditId == null);
        var correctedText = translation?.TranslatedText ?? correctedMessage.MessageText!;

        var spam = await _repository.GetSpamTextsAsync(Always);
        var legit = await _repository.GetLegitTextsAsync(Always);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(spam, Does.Not.Contain(correctedText), "an admin ham correction supersedes the spam scan");
            Assert.That(legit, Does.Contain(correctedText));
        }
    }

    [Test]
    public async Task ScanCheckResults_ExcludeDecisions()
    {
        var scans = await _repository.GetScanCheckResultsAsync(Always);
        await using var ctx = _helper.GetDbContext();
        var decisionIds = await ctx.DetectionResults.Where(d => d.Source != 0).Select(d => d.Id).ToListAsync();
        Assert.That(scans.Select(s => s.Id), Has.None.AnyOf(decisionIds));
    }

    [Test]
    public async Task GetCountsAsync_CountsTrainingSpam_NonSpamVerdicts_AndContentScansOnly()
    {
        await using var ctx = _helper.GetDbContext();
        var verdicts = await ctx.MessageVerdicts.Select(v => v.Classification).ToListAsync();
        var trainingSpam = verdicts.Count(c => VerdictClassifications.TrainingSpamValues.Contains(c));
        var notSpam = verdicts.Count(c => !VerdictClassifications.SpamValues.Contains(c));
        var untrainedSpam = verdicts.Count(c => c == (int)VerdictClassification.UntrainedSpam);
        var contentScans = await ctx.DetectionResults.CountAsync(d => d.Source == (int)VerdictSource.ContentScan);
        var allRows = await ctx.DetectionResults.CountAsync();
        Assert.That(untrainedSpam, Is.GreaterThan(0), "canonical precondition: e.g. the FileScan-beside-scan message");

        var counts = await _repository.GetCountsAsync(Always);

        using (Assert.EnterMultipleScope())
        {
            // Spam corpus = training spam (explicit + implicit); untrained spam is in neither corpus.
            Assert.That(counts.SpamSamples, Is.EqualTo(trainingSpam));
            // Legit corpus = every non-spam verdict, Unscanned and Untrained ham included.
            Assert.That(counts.LegitMessages, Is.EqualTo(notSpam));
            Assert.That(verdicts.Count - counts.SpamSamples - counts.LegitMessages, Is.EqualTo(untrainedSpam));
            // Scans exclude decisions and file scans.
            Assert.That(counts.ScanResults, Is.EqualTo(contentScans));
            Assert.That(counts.ScanResults, Is.LessThan(allRows));
        }
    }

    [Test]
    public async Task GetCountsAsync_SinceAfterAllData_IsZero()
    {
        await using var ctx = _helper.GetDbContext();
        var newestMessage = await ctx.Messages.MaxAsync(m => m.Timestamp);
        var newestDetection = await ctx.DetectionResults.MaxAsync(d => d.DetectedAt);
        var since = (newestMessage > newestDetection ? newestMessage : newestDetection).AddSeconds(1);

        var counts = await _repository.GetCountsAsync(since);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(counts.SpamSamples, Is.Zero);
            Assert.That(counts.LegitMessages, Is.Zero);
            Assert.That(counts.ScanResults, Is.Zero);
        }
    }
}
