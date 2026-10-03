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
/// (message_verdicts). Anchors: <see cref="GoldenDatasetConstants.Verdicts.PhotoFeaturesMsgId"/> and
/// <see cref="GoldenDatasetConstants.Verdicts.VideoFeaturesMsgId"/>.
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

        // The anchor is an AutoBan decision: spam, and not an admin-verified ham anchor.
        Assert.That(samples, Has.Some.Matches<(PhotoFeatures Features, VerdictClassification Classification)>(
            s => s.Classification == VerdictClassification.ExplicitSpam
                 && s.Features.Hash.SequenceEqual(Convert.FromBase64String("8J8PDw8PH/8="))));
    }

    [Test]
    public async Task GetRecentVideoSamplesAsync_ReturnsExplicitSpamVideoWithKeyframes()
    {
        await using (var ctx = _helper.GetDbContext())
        {
            // guard the canonical edit
            var video = await ctx.Messages.SingleAsync(m => m.MessageId == GoldenDatasetConstants.Verdicts.VideoFeaturesMsgId
                && m.ChatId == GoldenDatasetConstants.Chats.MainChatId);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(video.MediaFeatures, Is.TypeOf<VideoFeaturesDto>());
                Assert.That(video.PhotoFileId, Is.Null);
            }
        }

        var samples = await _repository.GetRecentVideoSamplesAsync(limit: 100);

        var expectedHashes = GoldenDatasetConstants.Verdicts.VideoFeaturesKeyframeHashes.Select(Convert.FromBase64String).ToArray();
        var anchor = samples.Where(s => s.Features.Keyframes.Count == expectedHashes.Length
                && s.Features.Keyframes.Select(k => k.Hash).Zip(expectedHashes).All(p => p.First.SequenceEqual(p.Second)))
            .ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(anchor, Has.Count.EqualTo(1));
            // The anchor's current verdict is a legacy spam decision.
            Assert.That(anchor.Single().Classification, Is.EqualTo(VerdictClassification.ExplicitSpam));
            Assert.That(anchor.Single().Features.Keyframes.Select(k => k.Position), Is.EqualTo(new[] { 0.1, 0.5, 0.9 }));
        }
    }
}
