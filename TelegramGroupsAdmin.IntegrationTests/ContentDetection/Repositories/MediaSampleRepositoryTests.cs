using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.IntegrationTests.TestData;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.ContentDetection.Repositories;

/// <summary>
/// Layer 1 media samples come from messages (media_features) joined to their current verdict
/// (message_verdicts). Anchor: <see cref="GoldenDatasetConstants.Verdicts.PhotoFeaturesMsgId"/>.
/// </summary>
[TestFixture]
public class MediaSampleRepositoryTests
{
    private MigrationTestHelper _helper = null!;
    private ServiceProvider _provider = null!;
    private IMediaSampleRepository _repository = null!;

    [SetUp]
    public async Task SetUp()
    {
        _helper = new MigrationTestHelper();
        await _helper.CreateDatabaseFromGoldenTemplateAsync();
        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(o => o.UseNpgsql(_helper.ConnectionString));
        services.AddScoped<IMediaSampleRepository, MediaSampleRepository>();
        _provider = services.BuildServiceProvider();
        _repository = _provider.GetRequiredService<IMediaSampleRepository>();
    }

    [TearDown]
    public void TearDown()
    {
        _provider.Dispose();
        _helper.Dispose();
    }

    [Test]
    public async Task GetRecentPhotoSamplesAsync_ReturnsExplicitSpamPhotoWithFeatures()
    {
        await using (var ctx = _helper.GetDbContext())
        {
            // guard the canonical edit
            Assert.That((await ctx.Messages.SingleAsync(m => m.MessageId == GoldenDatasetConstants.Verdicts.PhotoFeaturesMsgId
                && m.ChatId == GoldenDatasetConstants.Chats.MainChatId)).MediaFeatures, Is.TypeOf<PhotoFeaturesDto>());
        }

        var samples = await _repository.GetRecentPhotoSamplesAsync(limit: 100);

        Assert.That(samples, Has.Some.Matches<(PhotoFeatures Features, bool IsSpam)>(
            s => s.IsSpam && s.Features.Hash.SequenceEqual(Convert.FromBase64String("8J8PDw8PH/8="))));
    }
}
