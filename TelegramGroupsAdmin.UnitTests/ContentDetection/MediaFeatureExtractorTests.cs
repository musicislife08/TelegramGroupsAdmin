using NSubstitute;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.Services;

namespace TelegramGroupsAdmin.UnitTests.ContentDetection;

[TestFixture]
public class MediaFeatureExtractorTests
{
    [Test]
    public async Task ExtractPhotoAsync_MissingFile_ReturnsNullWithoutThrowing()
    {
        var hashes = Substitute.For<IPhotoHashService>();
        var extractor = new MediaFeatureExtractor(hashes, Substitute.For<IVideoFrameExtractionService>());

        var features = await extractor.ExtractPhotoAsync(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.jpg"));

        Assert.That(features, Is.Null);
        await hashes.DidNotReceiveWithAnyArgs().ComputePhotoHashAsync(default!);
    }

    [Test]
    public async Task FromFramesAsync_HashesEveryFrame_KeepsPositions()
    {
        var hashes = Substitute.For<IPhotoHashService>();
        hashes.ComputePhotoHashAsync(Arg.Any<string>()).Returns(new byte[8]);
        var extractor = new MediaFeatureExtractor(hashes, Substitute.For<IVideoFrameExtractionService>());

        var features = await extractor.FromFramesAsync([new ExtractedFrame("a.jpg", 0.1, 50, false), new ExtractedFrame("b.jpg", 0.9, 50, false)]);

        Assert.That(features!.Keyframes.Select(k => k.Position), Is.EqualTo(new[] { 0.1, 0.9 }));
    }
}
