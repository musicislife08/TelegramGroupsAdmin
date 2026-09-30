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
/// early. A ham match skips OCR/Vision only when the anchor is admin-verified (ExplicitHam) and the
/// similarity reaches HamSkipThreshold; every other ham match falls through to Layer 2 (OCR text
/// checks) and Layer 3 (Vision).
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

    // Defaults: HashSimilarityThreshold 0.85, HamSkipThreshold 0.95.
    [TestCase(VerdictClassification.ExplicitHam, 0.97, true, TestName = "Image: admin-verified ham >= HamSkipThreshold skips OCR/Vision")]
    [TestCase(VerdictClassification.ExplicitHam, 0.90, false, TestName = "Image: admin-verified ham between thresholds falls through")]
    [TestCase(VerdictClassification.ImplicitHam, 0.99, false, TestName = "Image: auto-scanned ham >= HamSkipThreshold falls through")]
    public async Task Image_HamHashMatch(VerdictClassification anchor, double similarity, bool skips)
    {
        var check = CreateImageCheck();
        SetPhotoSample(anchor, similarity);

        var response = await check.CheckAsync(CreateImageRequest());

        await AssertHamOutcome(response, skips, AIFeatureType.ImageAnalysis);
    }

    [TestCase(VerdictClassification.ExplicitSpam)]
    [TestCase(VerdictClassification.ImplicitSpam)]
    public async Task Image_SpamHashMatch_ReturnsHashScoreWithoutOcrChecksOrVision(VerdictClassification anchor)
    {
        var check = CreateImageCheck();
        SetPhotoSample(anchor, similarity: 0.90);

        var response = await check.CheckAsync(CreateImageRequest());

        await AssertSpamOutcome(response, _config.ImageSpam.HashMatchConfidence);
    }

    [TestCase(VerdictClassification.ExplicitHam, 0.97, true, TestName = "Video: admin-verified ham >= HamSkipThreshold skips OCR/Vision")]
    [TestCase(VerdictClassification.ExplicitHam, 0.90, false, TestName = "Video: admin-verified ham between thresholds falls through")]
    [TestCase(VerdictClassification.ImplicitHam, 0.99, false, TestName = "Video: auto-scanned ham >= HamSkipThreshold falls through")]
    public async Task Video_HamHashMatch(VerdictClassification anchor, double similarity, bool skips)
    {
        var check = CreateVideoCheck();
        SetVideoSample(anchor, similarity);

        var response = await check.CheckAsync(CreateVideoRequest());

        await AssertHamOutcome(response, skips, AIFeatureType.VideoAnalysis);
    }

    [TestCase(VerdictClassification.ExplicitSpam)]
    [TestCase(VerdictClassification.ImplicitSpam)]
    public async Task Video_SpamHashMatch_ReturnsHashScoreWithoutOcrChecksOrVision(VerdictClassification anchor)
    {
        var check = CreateVideoCheck();
        SetVideoSample(anchor, similarity: 0.90);

        var response = await check.CheckAsync(CreateVideoRequest());

        await AssertSpamOutcome(response, _config.VideoSpam.HashMatchConfidence);
    }

    // #521: the OCR text re-runs the engine, whose AI veto must still leave the message out of its own history
    [Test]
    public async Task Image_OcrRequest_CarriesMessageId()
    {
        var check = CreateImageCheck();
        SetPhotoSample(VerdictClassification.ImplicitHam, similarity: 0.99);

        await check.CheckAsync(CreateImageRequest(messageId: 77310));

        await _engine.Received(1).CheckMessageAsync(
            Arg.Is<ContentCheckRequest>(r => r!.MessageId == 77310), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Video_OcrRequest_CarriesMessageId()
    {
        var check = CreateVideoCheck();
        SetVideoSample(VerdictClassification.ImplicitHam, similarity: 0.99);

        await check.CheckAsync(CreateVideoRequest(messageId: 77310));

        await _engine.Received(1).CheckMessageAsync(
            Arg.Is<ContentCheckRequest>(r => r!.MessageId == 77310), Arg.Any<CancellationToken>());
    }

    private async Task AssertHamOutcome(ContentCheckResponseV2 response, bool skips, AIFeatureType visionFeature)
    {
        if (skips)
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(response.Abstained, Is.True);
                Assert.That(response.Score, Is.Zero);
                Assert.That(response.Details, Does.Contain("known ham sample"));
            }
            await _engine.DidNotReceiveWithAnyArgs().CheckMessageAsync(null!, default);
            await DidNotReceiveVisionCall();
            return;
        }

        await _engine.Received(1).CheckMessageAsync(
            Arg.Is<ContentCheckRequest>(r => r!.Message == OcrText), Arg.Any<CancellationToken>());
        await ReceivedVisionCall(visionFeature);
        Assert.That(response.Details, Does.Not.Contain("ham sample"));
    }

    private async Task AssertSpamOutcome(ContentCheckResponseV2 response, double expectedScore)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Abstained, Is.False);
            Assert.That(response.Score, Is.EqualTo(expectedScore));
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

    private void SetPhotoSample(VerdictClassification anchor, double similarity)
    {
        _photoHashService.CompareHashes(Arg.Any<byte[]>(), Arg.Any<byte[]>()).Returns(similarity);
        _extractor.ExtractPhotoAsync(Arg.Any<string>()).Returns(new PhotoFeatures([1, 2, 3]));
        _mediaSamples.GetRecentPhotoSamplesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([(new PhotoFeatures([1, 2, 3]), anchor)]);
    }

    private void SetVideoSample(VerdictClassification anchor, double similarity)
    {
        _photoHashService.CompareHashes(Arg.Any<byte[]>(), Arg.Any<byte[]>()).Returns(similarity);
        var features = new VideoFeatures([new KeyframeFeature(0.5, [1, 2, 3])]);
        _extractor.FromFramesAsync(Arg.Any<IReadOnlyList<ExtractedFrame>>()).Returns(features);
        _mediaSamples.GetRecentVideoSamplesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([(features, anchor)]);
    }

    private ImageCheckRequest CreateImageRequest(int? messageId = null) => new()
    {
        Message = "caption",
        User = UserIdentity.FromId(123),
        Chat = ChatIdentity.FromId(-100),
        MessageId = messageId,
        CancellationToken = CancellationToken.None,
        PhotoFileId = "photo-file-id",
        PhotoLocalPath = WriteTempFile("photo.jpg"),
        CustomPrompt = null
    };

    private VideoCheckRequest CreateVideoRequest(int? messageId = null)
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
            MessageId = messageId,
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
