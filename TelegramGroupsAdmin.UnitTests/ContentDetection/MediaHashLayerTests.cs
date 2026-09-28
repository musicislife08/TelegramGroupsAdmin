using Microsoft.Extensions.Logging;
using NSubstitute;
using TelegramGroupsAdmin.AI.Services;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Models.ContentDetection;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.ContentDetection.Checks;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;

namespace TelegramGroupsAdmin.UnitTests.ContentDetection;

/// <summary>
/// Layer 1 (hash similarity) of the image and video checks: a spam match is decisive and returns
/// early; a ham match is not, and falls through to Layer 2 (OCR text checks) and Layer 3 (Vision).
/// </summary>
[TestFixture]
public class MediaHashLayerTests
{
    private const string OcrText = "Claim your free airdrop tokens now at this link";
    private const string VisionJson = """{"spam": false, "score": 0.5, "reason": "benign", "patterns_detected": []}""";

    private IChatService _chatService = null!;
    private IImageTextExtractionService _ocr = null!;
    private IContentDetectionEngine _engine = null!;
    private IServiceProvider _serviceProvider = null!;
    private IConfigService _configService = null!;
    private IPhotoHashService _photoHashService = null!;
    private IMediaFeatureExtractor _extractor = null!;
    private IMediaSampleRepository _mediaSamples = null!;
    private IVideoFrameExtractionService _frames = null!;
    private ContentDetectionConfig _config = null!;
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"media-hash-layer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _chatService = Substitute.For<IChatService>();
        _ocr = Substitute.For<IImageTextExtractionService>();
        _engine = Substitute.For<IContentDetectionEngine>();
        _serviceProvider = Substitute.For<IServiceProvider>();
        _configService = Substitute.For<IConfigService>();
        _photoHashService = Substitute.For<IPhotoHashService>();
        _extractor = Substitute.For<IMediaFeatureExtractor>();
        _mediaSamples = Substitute.For<IMediaSampleRepository>();
        _frames = Substitute.For<IVideoFrameExtractionService>();

        _config = new ContentDetectionConfig();
        _configService.GetEffectiveContentDetectionAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<ContentDetectionConfig>(_config));

        _serviceProvider.GetService(typeof(IContentDetectionEngine)).Returns(_engine);

        // Every hash comparison is an exact match, so the sample's label decides Layer 1.
        _photoHashService.CompareHashes(Arg.Any<byte[]>(), Arg.Any<byte[]>()).Returns(1.0);

        // Layer 2: OCR finds text; the text checks are uncertain, so Layer 3 runs.
        _ocr.IsAvailable.Returns(true);
        _ocr.ExtractTextAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(OcrText);
        _engine.CheckMessageAsync(Arg.Any<ContentCheckRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ContentDetectionResult { IsSpam = false, TotalScore = 0.5 });

        // Layer 3: Vision is available and answers.
        _chatService.IsFeatureAvailableAsync(Arg.Any<AIFeatureType>(), Arg.Any<CancellationToken>()).Returns(true);
        _chatService.GetVisionCompletionAsync(
                Arg.Any<AIFeatureType>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatCompletionResult { Content = VisionJson });
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Test]
    public async Task Image_HamHashMatch_ContinuesToOcrAndVision()
    {
        var check = CreateImageCheck();
        SetPhotoSample(isSpam: false);

        var response = await check.CheckAsync(CreateImageRequest());

        await _engine.Received(1).CheckMessageAsync(
            Arg.Is<ContentCheckRequest>(r => r!.Message == OcrText), Arg.Any<CancellationToken>());
        await ReceivedVisionCall(AIFeatureType.ImageAnalysis);
        Assert.That(response.Details, Does.Not.Contain("ham sample"));
    }

    [Test]
    public async Task Image_SpamHashMatch_ReturnsHashScoreWithoutOcrChecksOrVision()
    {
        var check = CreateImageCheck();
        SetPhotoSample(isSpam: true);

        var response = await check.CheckAsync(CreateImageRequest());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Abstained, Is.False);
            Assert.That(response.Score, Is.EqualTo(_config.ImageSpam.HashMatchConfidence));
            Assert.That(response.Details, Does.Contain("known spam sample"));
        }
        await _engine.DidNotReceiveWithAnyArgs().CheckMessageAsync(null!, default);
        await DidNotReceiveVisionCall();
    }

    [Test]
    public async Task Video_HamHashMatch_ContinuesToOcrAndVision()
    {
        var check = CreateVideoCheck();
        SetVideoSample(isSpam: false);

        var response = await check.CheckAsync(CreateVideoRequest());

        await _engine.Received(1).CheckMessageAsync(
            Arg.Is<ContentCheckRequest>(r => r!.Message == OcrText), Arg.Any<CancellationToken>());
        await ReceivedVisionCall(AIFeatureType.VideoAnalysis);
        Assert.That(response.Details, Does.Not.Contain("ham sample"));
    }

    [Test]
    public async Task Video_SpamHashMatch_ReturnsHashScoreWithoutOcrChecksOrVision()
    {
        var check = CreateVideoCheck();
        SetVideoSample(isSpam: true);

        var response = await check.CheckAsync(CreateVideoRequest());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Abstained, Is.False);
            Assert.That(response.Score, Is.EqualTo(_config.VideoSpam.HashMatchConfidence));
            Assert.That(response.Details, Does.Contain("known spam sample"));
        }
        await _engine.DidNotReceiveWithAnyArgs().CheckMessageAsync(null!, default);
        await DidNotReceiveVisionCall();
    }

    private ImageContentCheckV2 CreateImageCheck() => new(
        Substitute.For<ILogger<ImageContentCheckV2>>(), _chatService, _ocr, _serviceProvider, _configService,
        _photoHashService, _extractor, _mediaSamples);

    private VideoContentCheckV2 CreateVideoCheck() => new(
        Substitute.For<ILogger<VideoContentCheckV2>>(), _chatService, _frames, _ocr, _serviceProvider, _configService,
        _photoHashService, _extractor, _mediaSamples);

    private void SetPhotoSample(bool isSpam)
    {
        _extractor.ExtractPhotoAsync(Arg.Any<string>()).Returns(new PhotoFeatures([1, 2, 3]));
        _mediaSamples.GetRecentPhotoSamplesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([(new PhotoFeatures([1, 2, 3]), isSpam)]);
    }

    private void SetVideoSample(bool isSpam)
    {
        var features = new VideoFeatures([new KeyframeFeature(0.5, [1, 2, 3])]);
        _extractor.FromFramesAsync(Arg.Any<IReadOnlyList<ExtractedFrame>>()).Returns(features);
        _mediaSamples.GetRecentVideoSamplesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([(features, isSpam)]);
    }

    private ImageCheckRequest CreateImageRequest() => new()
    {
        Message = "caption",
        User = UserIdentity.FromId(123),
        Chat = ChatIdentity.FromId(-100),
        CancellationToken = CancellationToken.None,
        PhotoFileId = "photo-file-id",
        PhotoLocalPath = WriteTempFile("photo.jpg"),
        CustomPrompt = null
    };

    private VideoCheckRequest CreateVideoRequest()
    {
        _frames.IsAvailable.Returns(true);
        var framePath = WriteTempFile("frame.jpg");
        _frames.ExtractKeyframesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([new ExtractedFrame(framePath, 0.5, 0.5, false)]);

        return new VideoCheckRequest
        {
            Message = "caption",
            User = UserIdentity.FromId(123),
            Chat = ChatIdentity.FromId(-100),
            CancellationToken = CancellationToken.None,
            VideoLocalPath = WriteTempFile("video.mp4"),
            CustomPrompt = null
        };
    }

    private string WriteTempFile(string name)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, [0xFF, 0xD8, 0xFF]);
        return path;
    }

    private Task<ChatCompletionResult?> ReceivedVisionCall(AIFeatureType feature) =>
        _chatService.Received(1).GetVisionCompletionAsync(
            feature, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<string>(),
            Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>());

    private Task<ChatCompletionResult?> DidNotReceiveVisionCall() =>
        _chatService.DidNotReceive().GetVisionCompletionAsync(
            Arg.Any<AIFeatureType>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<string>(),
            Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>());
}
