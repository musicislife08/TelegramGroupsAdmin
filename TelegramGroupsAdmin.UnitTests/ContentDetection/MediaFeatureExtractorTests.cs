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

    [Test]
    public async Task FromFramesAsync_UnhashableFrameIsSkipped_AllUnhashableReturnsNull()
    {
        var hashes = Substitute.For<IPhotoHashService>();
        hashes.ComputePhotoHashAsync("a.jpg").Returns(new byte[8]);
        hashes.ComputePhotoHashAsync("b.jpg").Returns((byte[]?)null);
        var extractor = new MediaFeatureExtractor(hashes, Substitute.For<IVideoFrameExtractionService>());

        var partial = await extractor.FromFramesAsync([new ExtractedFrame("a.jpg", 0.1, 50, false), new ExtractedFrame("b.jpg", 0.9, 50, false)]);
        var none = await extractor.FromFramesAsync([new ExtractedFrame("b.jpg", 0.5, 50, false)]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(partial!.Keyframes.Select(k => k.Position), Is.EqualTo(new[] { 0.1 }));
            Assert.That(none, Is.Null);
        }
    }

    [Test]
    public async Task ExtractVideoAsync_HashesKeyframes_AndDeletesTheFrameFiles()
    {
        var video = Path.GetTempFileName();
        var frame = Path.GetTempFileName();
        try
        {
            var hashes = Substitute.For<IPhotoHashService>();
            hashes.ComputePhotoHashAsync(frame).Returns(new byte[8]);
            var frames = Substitute.For<IVideoFrameExtractionService>();
            frames.IsAvailable.Returns(true);
            frames.ExtractKeyframesAsync(video, Arg.Any<CancellationToken>()).Returns([new ExtractedFrame(frame, 0.5, 50, false)]);

            var features = await new MediaFeatureExtractor(hashes, frames).ExtractVideoAsync(video);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(features!.Keyframes, Has.Count.EqualTo(1));
                Assert.That(File.Exists(frame), Is.False, "extracted frames are temp files and must be cleaned up");
            }
        }
        finally
        {
            File.Delete(video);
            File.Delete(frame);
        }
    }

    [Test]
    public void ExtractVideoAsync_HashingThrows_StillDeletesTheFrameFiles()
    {
        var video = Path.GetTempFileName();
        var frame = Path.GetTempFileName();
        try
        {
            var hashes = Substitute.For<IPhotoHashService>();
            hashes.ComputePhotoHashAsync(frame).Returns<byte[]?>(_ => throw new IOException("disk"));
            var frames = Substitute.For<IVideoFrameExtractionService>();
            frames.IsAvailable.Returns(true);
            frames.ExtractKeyframesAsync(video, Arg.Any<CancellationToken>()).Returns([new ExtractedFrame(frame, 0.5, 50, false)]);

            Assert.ThrowsAsync<IOException>(() => new MediaFeatureExtractor(hashes, frames).ExtractVideoAsync(video));
            Assert.That(File.Exists(frame), Is.False);
        }
        finally
        {
            File.Delete(video);
            File.Delete(frame);
        }
    }

    [Test]
    public async Task ExtractVideoAsync_FfmpegUnavailable_ReturnsNull()
    {
        var video = Path.GetTempFileName();
        try
        {
            var frames = Substitute.For<IVideoFrameExtractionService>();
            frames.IsAvailable.Returns(false);

            var features = await new MediaFeatureExtractor(Substitute.For<IPhotoHashService>(), frames).ExtractVideoAsync(video);

            Assert.That(features, Is.Null);
            await frames.DidNotReceiveWithAnyArgs().ExtractKeyframesAsync(default!, default);
        }
        finally
        {
            File.Delete(video);
        }
    }
}
