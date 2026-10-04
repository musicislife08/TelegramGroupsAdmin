using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Configuration.Models.ContentDetection;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.BackgroundServices;
using TelegramGroupsAdmin.Telegram.Services.Bot;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.BackgroundServices;

[TestFixture]
public class DetectionActionServiceTests
{
    private IReportService _reportService = null!;
    private DetectionActionService _service = null!;

    [SetUp]
    public void SetUp()
    {
        var configService = Substitute.For<IConfigService>();
        configService.GetEffectiveContentDetectionAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(new ContentDetectionConfig()); // defaults: ReviewQueue 2.5, AutoBan 4.0

        _reportService = Substitute.For<IReportService>();

        var services = new ServiceCollection();
        services.AddScoped(_ => configService);
        services.AddScoped(_ => _reportService);
        services.AddScoped(_ => Substitute.For<IBotModerationService>());

        _service = new DetectionActionService(
            services.BuildServiceProvider(), new PipelineMetrics(), NullLogger<DetectionActionService>.Instance);
    }

    [Test]
    public async Task HandleSpamDetectionActionsAsync_ScoreExactlyAtReviewThreshold_CreatesReport()
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
            TotalScore = 2.5,
            CheckResults = [new ContentCheckResponseV2 { CheckName = CheckName.StopWords, Score = 2.5, Abstained = false, Details = "x" }]
        };
        var record = new DetectionResultRecord { AddedBy = Actor.AutoDetection, Reason = "Borderline" };

        await _service.HandleSpamDetectionActionsAsync(message, UserIdentity.ForTest(7, "Tester"), spamResult, record);

        await _reportService.Received(1).CreateReportAsync(
            Arg.Any<Report>(), message, Actor.AutoDetection, Arg.Any<CancellationToken>());
    }
}
