using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TelegramGroupsAdmin.ContentDetection.Repositories;
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
}
