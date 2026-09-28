using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TelegramGroupsAdmin.ContentDetection.Repositories;
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
}
