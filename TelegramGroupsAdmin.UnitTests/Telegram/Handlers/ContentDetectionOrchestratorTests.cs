using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models.ContentDetection;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Handlers;
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.BackgroundServices;
using TelegramGroupsAdmin.Telegram.Services.Bot;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Handlers;

/// <summary>
/// ContentDetectionOrchestrator: media features computed by a check are stored on the message, and
/// storing them is non-critical: it must never skip moderation.
/// </summary>
[TestFixture]
public class ContentDetectionOrchestratorTests
{
    private IContentCheckCoordinator _coordinator = null!;
    private IDetectionResultsRepository _detectionResults = null!;
    private IMessageHistoryRepository _messageHistory = null!;
    private IReportService _reportService = null!;
    private ServiceProvider _provider = null!;
    private ContentDetectionOrchestrator _orchestrator = null!;

    [SetUp]
    public void SetUp()
    {
        _coordinator = Substitute.For<IContentCheckCoordinator>();
        _detectionResults = Substitute.For<IDetectionResultsRepository>();
        _messageHistory = Substitute.For<IMessageHistoryRepository>();
        _reportService = Substitute.For<IReportService>();

        var configService = Substitute.For<IConfigService>();
        configService.GetEffectiveContentDetectionAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(new ContentDetectionConfig()); // defaults: ReviewQueue 2.5, AutoBan 4.0

        var services = new ServiceCollection();
        services.AddScoped(_ => _coordinator);
        services.AddScoped(_ => _detectionResults);
        services.AddScoped(_ => _messageHistory);
        services.AddScoped(_ => configService);
        services.AddScoped(_ => _reportService);
        services.AddScoped(_ => Substitute.For<IBotModerationService>());
        services.AddSingleton(Options.Create(new AppOptions()));
        _provider = services.BuildServiceProvider();

        var actionService = new DetectionActionService(_provider, new PipelineMetrics(), NullLogger<DetectionActionService>.Instance);
        _orchestrator = new ContentDetectionOrchestrator(_provider, actionService, NullLogger<ContentDetectionOrchestrator>.Instance);
    }

    [TearDown]
    public void TearDown() => _provider.Dispose();

    [Test]
    public async Task RunDetectionAsync_StoringMediaFeaturesFails_StillTakesModerationAction()
    {
        var message = new Message
        {
            Id = 42,
            Chat = new Chat { Id = -1001, Type = ChatType.Supergroup, Title = "Test" },
            From = new User { Id = 7, FirstName = "Tester" }
        };
        var spamResult = new ContentDetectionResult
        {
            IsSpam = true,
            TotalScore = 2.5, // borderline: routes to the review queue
            CheckResults =
            [
                new ContentCheckResponseV2
                {
                    CheckName = CheckName.ImageSpam, Score = 2.5, Abstained = false, Details = "x",
                    MediaFeatures = new PhotoFeatures([1, 2, 3])
                }
            ]
        };
        _coordinator.CheckAsync(Arg.Any<ContentCheckRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ContentCheckCoordinatorResult { SpamResult = spamResult });
        _detectionResults.RecordScanAsync(42, -1001, spamResult, 0, Arg.Any<CancellationToken>())
            .Returns(new DetectionResultRecord { AddedBy = Actor.AutoDetection, Reason = "Borderline" });
        _messageHistory.SetMediaFeaturesAsync(42, -1001, Arg.Any<MediaFeatures>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("db down"));

        await _orchestrator.RunDetectionAsync(message, text: "hello", photoLocalPath: null, editVersion: 0);

        await _messageHistory.Received(1).SetMediaFeaturesAsync(42, -1001, Arg.Any<MediaFeatures>(), Arg.Any<CancellationToken>());
        await _reportService.Received(1).CreateReportAsync(
            Arg.Any<Report>(), message, Actor.AutoDetection, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RunDetectionAsync_CheckProducesMediaFeatures_StoresTheFirstOnTheMessage()
    {
        var message = CreateMessage();
        var videoFeatures = new VideoFeatures([new KeyframeFeature(0.5, [9, 8, 7, 6, 5, 4, 3, 2])]);
        var spamResult = BorderlineSpamResult(
            new ContentCheckResponseV2 { CheckName = CheckName.StopWords, Score = 0.5, Abstained = false, Details = "no media" },
            new ContentCheckResponseV2
            {
                CheckName = CheckName.VideoSpam, Score = 2.0, Abstained = false, Details = "x",
                MediaFeatures = videoFeatures
            },
            new ContentCheckResponseV2
            {
                CheckName = CheckName.ImageSpam, Score = 0, Abstained = true, Details = "later features are ignored",
                MediaFeatures = new PhotoFeatures([1, 2, 3])
            });
        ArrangeScan(spamResult);

        await _orchestrator.RunDetectionAsync(message, text: "hello", photoLocalPath: null, editVersion: 0);

        await _messageHistory.Received(1).SetMediaFeaturesAsync(42, -1001, Arg.Any<MediaFeatures>(), Arg.Any<CancellationToken>());
        await _messageHistory.Received(1).SetMediaFeaturesAsync(42, -1001, videoFeatures, Arg.Any<CancellationToken>());
        await _reportService.Received(1).CreateReportAsync(
            Arg.Any<Report>(), message, Actor.AutoDetection, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RunDetectionAsync_NoCheckProducesMediaFeatures_DoesNotStoreFeatures()
    {
        var message = CreateMessage();
        var spamResult = BorderlineSpamResult(
            new ContentCheckResponseV2 { CheckName = CheckName.StopWords, Score = 2.5, Abstained = false, Details = "text only" });
        ArrangeScan(spamResult);

        await _orchestrator.RunDetectionAsync(message, text: "hello", photoLocalPath: null, editVersion: 0);

        await _messageHistory.DidNotReceiveWithAnyArgs().SetMediaFeaturesAsync(default, default, default!, default);
        // Moderation ran, so the flow reached (and passed) the media-features step rather than bailing out early.
        await _reportService.Received(1).CreateReportAsync(
            Arg.Any<Report>(), message, Actor.AutoDetection, Arg.Any<CancellationToken>());
    }

    private static Message CreateMessage() => new()
    {
        Id = 42,
        Chat = new Chat { Id = -1001, Type = ChatType.Supergroup, Title = "Test" },
        From = new User { Id = 7, FirstName = "Tester" }
    };

    /// <summary>Score 2.5 with the default config routes to the review queue (a report, no ban).</summary>
    private static ContentDetectionResult BorderlineSpamResult(params ContentCheckResponseV2[] checks) => new()
    {
        IsSpam = true,
        TotalScore = 2.5,
        CheckResults = [.. checks]
    };

    private void ArrangeScan(ContentDetectionResult spamResult)
    {
        _coordinator.CheckAsync(Arg.Any<ContentCheckRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ContentCheckCoordinatorResult { SpamResult = spamResult });
        _detectionResults.RecordScanAsync(42, -1001, spamResult, 0, Arg.Any<CancellationToken>())
            .Returns(new DetectionResultRecord { AddedBy = Actor.AutoDetection, Reason = "Borderline" });
    }
}
