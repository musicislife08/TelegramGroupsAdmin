using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestData;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Services.TrainingData;

namespace TelegramGroupsAdmin.IntegrationTests.Services;

[TestFixture]
public class TrainingDataServiceTests
{
    private MigrationTestHelper _helper = null!;
    private ServiceProvider _provider = null!;
    private ITrainingDataService _service = null!;
    private IDetectionResultsRepository _repository = null!;
    private IJobTriggerService _jobTriggerService = null!;
    private static readonly Actor Owner = Actor.FromWebUser(GoldenDatasetConstants.WebUsers.OwnerId);

    [SetUp]
    public async Task SetUp()
    {
        _helper = new MigrationTestHelper();
        await _helper.CreateDatabaseFromGoldenTemplateAsync();
        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(o => o.UseNpgsql(_helper.ConnectionString));
        services.AddLogging();
        services.AddScoped<IDetectionResultsRepository, DetectionResultsRepository>();
        services.AddScoped(_ => Substitute.For<IJobTriggerService>());
        services.AddScoped<ITrainingDataService, TrainingDataService>();
        _provider = services.BuildServiceProvider();
        _service = _provider.GetRequiredService<ITrainingDataService>();
        _repository = _provider.GetRequiredService<IDetectionResultsRepository>();
        _jobTriggerService = _provider.GetRequiredService<IJobTriggerService>();
    }

    [TearDown]
    public void TearDown()
    {
        _provider.Dispose();
        _helper.Dispose();
    }

    [Test]
    public async Task ExcludeAsync_ExplicitSpam_BecomesUntrainedSpam_AndLeavesTheList()
    {
        var chatId = GoldenDatasetConstants.Chats.MainChatId;
        var msgId = GoldenDatasetConstants.Verdicts.AutoBanMsgId;
        Assert.That((await _service.GetSamplesAsync()).Any(s => s.MessageId == msgId && s.ChatId == chatId), Is.True,
            "canonical precondition: auto-banned message is a curated sample");

        await _service.ExcludeAsync(msgId, chatId, Owner);

        var verdict = await _repository.GetCurrentVerdictAsync(msgId, chatId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(verdict!.Classification, Is.EqualTo(VerdictClassification.UntrainedSpam));
            Assert.That(verdict.IsSpam, Is.True, "exclusion keeps the verdict");
            Assert.That((await _service.GetSamplesAsync()).Any(s => s.MessageId == msgId && s.ChatId == chatId), Is.False);
        }
    }

    [Test]
    public async Task ExcludeAsync_UnscannedMessage_BecomesUntrainedHam()
    {
        var chatId = GoldenDatasetConstants.Chats.MainChatId;
        var msgId = GoldenDatasetConstants.Verdicts.UnscannedMsgId;

        await _service.ExcludeAsync(msgId, chatId, Owner);

        Assert.That((await _repository.GetCurrentVerdictAsync(msgId, chatId))!.Classification,
            Is.EqualTo(VerdictClassification.UntrainedHam));
    }

    [Test]
    public async Task AddSampleAsync_CreatesExplicitSampleOnChatZero()
    {
        await _service.AddSampleAsync("Buy cheap followers at canonical-spam.test now", isSpam: true, Owner, null, null);

        var added = (await _service.GetSamplesAsync())
            .Single(s => s.ChatId == 0 && s.MessageText == "Buy cheap followers at canonical-spam.test now");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(added.Source, Is.EqualTo(VerdictSource.TrainingDataPage));
            Assert.That(added.Classification, Is.EqualTo(VerdictClassification.ExplicitSpam));
            Assert.That(added.AddedBy.Type, Is.EqualTo(ActorType.WebUser));
        }
    }

    [Test]
    public async Task ExcludeManyAsync_TwoCuratedSamples_BothLeaveTheListAndRetrainOnce()
    {
        var chatId = GoldenDatasetConstants.Chats.MainChatId;
        var spamMsgId = GoldenDatasetConstants.Verdicts.AutoBanMsgId;
        var hamMsgId = GoldenDatasetConstants.Verdicts.CorrectedToHamMsgId;

        var samplesBefore = await _service.GetSamplesAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(samplesBefore.Any(s => s.MessageId == spamMsgId && s.ChatId == chatId), Is.True,
                "canonical precondition: auto-banned message is a curated sample");
            Assert.That(samplesBefore.Any(s => s.MessageId == hamMsgId && s.ChatId == chatId), Is.True,
                "canonical precondition: corrected-to-ham message is a curated sample");
        }

        await _service.ExcludeManyAsync([(spamMsgId, chatId), (hamMsgId, chatId)], Owner);

        var spamVerdict = await _repository.GetCurrentVerdictAsync(spamMsgId, chatId);
        var hamVerdict = await _repository.GetCurrentVerdictAsync(hamMsgId, chatId);
        var samplesAfter = await _service.GetSamplesAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(spamVerdict!.Classification, Is.EqualTo(VerdictClassification.UntrainedSpam));
            Assert.That(hamVerdict!.Classification, Is.EqualTo(VerdictClassification.UntrainedHam));
            Assert.That(samplesAfter.Any(s => s.MessageId == spamMsgId && s.ChatId == chatId), Is.False);
            Assert.That(samplesAfter.Any(s => s.MessageId == hamMsgId && s.ChatId == chatId), Is.False);
        }

        await _jobTriggerService.Received(1).TriggerNowAsync(
            BackgroundJobNames.ClassifierRetraining, Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExcludeManyAsync_EmptyCollection_TriggersNoRetrain()
    {
        await _service.ExcludeManyAsync([], Owner);

        await _jobTriggerService.DidNotReceive().TriggerNowAsync(
            Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ReplaceSampleAsync_ExcludesTheOldSampleAndAddsTheEditedOneOnChatZero()
    {
        // The edit dialog replaces a sample: the original keeps its spam verdict but leaves training,
        // and the edited text (here relabelled ham) becomes a new manual sample.
        var chatId = GoldenDatasetConstants.Chats.MainChatId;
        var msgId = GoldenDatasetConstants.Verdicts.AutoBanMsgId;
        const string editedText = "Weekly meetup moved to Thursday at the canonical community hall";
        Assert.That((await _service.GetSamplesAsync()).Any(s => s.MessageId == msgId && s.ChatId == chatId), Is.True,
            "canonical precondition: auto-banned message is a curated sample");

        await _service.ReplaceSampleAsync(msgId, chatId, editedText, isSpam: false, Owner);

        var oldVerdict = await _repository.GetCurrentVerdictAsync(msgId, chatId);
        var samples = await _service.GetSamplesAsync();
        var replacement = samples.Single(s => s.ChatId == 0 && s.MessageText == editedText);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(oldVerdict!.Classification, Is.EqualTo(VerdictClassification.UntrainedSpam));
            Assert.That(oldVerdict.Source, Is.EqualTo(VerdictSource.TrainingExclude));
            Assert.That(samples.Any(s => s.MessageId == msgId && s.ChatId == chatId), Is.False);
            Assert.That(replacement.MessageId, Is.Negative, "manual samples use negative message ids");
            Assert.That(replacement.Source, Is.EqualTo(VerdictSource.TrainingDataPage));
            Assert.That(replacement.Classification, Is.EqualTo(VerdictClassification.ExplicitHam));
            Assert.That(replacement.AddedBy.Type, Is.EqualTo(ActorType.WebUser));
        }

        await _jobTriggerService.Received(1).TriggerNowAsync(
            BackgroundJobNames.ClassifierRetraining, Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ReplaceSampleAsync_UnknownMessage_ThrowsAndAddsNothing()
    {
        const string editedText = "This replacement must never be stored";
        await using var ctx = _helper.GetDbContext();
        var chatZeroMessagesBefore = await ctx.Messages.CountAsync(m => m.ChatId == 0);

        Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ReplaceSampleAsync(int.MaxValue, GoldenDatasetConstants.Chats.MainChatId, editedText, isSpam: true, Owner));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await ctx.Messages.CountAsync(m => m.ChatId == 0), Is.EqualTo(chatZeroMessagesBefore));
            Assert.That(await ctx.Messages.AnyAsync(m => m.MessageText == editedText), Is.False);
        }

        await _jobTriggerService.DidNotReceiveWithAnyArgs().TriggerNowAsync(
            Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
    }
}
