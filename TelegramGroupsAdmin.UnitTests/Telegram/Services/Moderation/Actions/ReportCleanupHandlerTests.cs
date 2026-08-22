using Microsoft.Extensions.Logging;
using NSubstitute;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.Moderation.Actions;

/// <summary>
/// Unit tests for ReportCleanupHandler.
/// The worker owns no policy — it closes what the orchestrator hands it and nothing else.
/// </summary>
[TestFixture]
public class ReportCleanupHandlerTests
{
    private static readonly UserIdentity TestUser = new(555L, "Test", null, "testuser");
    private static readonly ChatIdentity TestChat = new(-100123L, "TestChat");
    private static readonly Actor TestExecutor = Actor.AutoDetection;

    private IReportsRepository _reportsRepository = null!;
    private ReportCleanupHandler _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _reportsRepository = Substitute.For<IReportsRepository>();
        _sut = new ReportCleanupHandler(
            _reportsRepository,
            Substitute.For<ILogger<ReportCleanupHandler>>());
    }

    private static ReportBase Report(long id, ReportType type) => new()
    {
        Id = id,
        Type = type,
        Chat = TestChat,
        Status = ReportStatus.Pending
    };

    [Test]
    public async Task CloseOpenReportsAsync_ClosesEveryPendingReport()
    {
        _reportsRepository
            .GetPendingForUserAsync(TestUser.Id, null, Arg.Any<CancellationToken>())
            .Returns([Report(1, ReportType.ExamFailure), Report(2, ReportType.ProfileScanAlert)]);
        _reportsRepository
            .TryUpdateStatusAsync(Arg.Any<long>(), Arg.Any<ReportStatus>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var closed = await _sut.CloseOpenReportsAsync(
            TestUser, chat: null, TestExecutor, "Ban", excludeReportId: null);

        Assert.That(closed, Is.EqualTo(2));
        await _reportsRepository.Received(1).TryUpdateStatusAsync(
            1, ReportStatus.Reviewed, Arg.Any<string>(), "Auto-Ban", Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _reportsRepository.Received(1).TryUpdateStatusAsync(
            2, ReportStatus.Reviewed, Arg.Any<string>(), "Auto-Ban", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CloseOpenReportsAsync_SkipsTheOriginatingReport()
    {
        _reportsRepository
            .GetPendingForUserAsync(TestUser.Id, null, Arg.Any<CancellationToken>())
            .Returns([Report(1, ReportType.ProfileScanAlert), Report(2, ReportType.ExamFailure)]);
        _reportsRepository
            .TryUpdateStatusAsync(Arg.Any<long>(), Arg.Any<ReportStatus>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var closed = await _sut.CloseOpenReportsAsync(
            TestUser, chat: null, TestExecutor, "Ban", excludeReportId: 1);

        Assert.That(closed, Is.EqualTo(1));
        await _reportsRepository.DidNotReceive().TryUpdateStatusAsync(
            1, Arg.Any<ReportStatus>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CloseOpenReportsAsync_LostRaceIsNotCounted()
    {
        _reportsRepository
            .GetPendingForUserAsync(TestUser.Id, null, Arg.Any<CancellationToken>())
            .Returns([Report(1, ReportType.ExamFailure)]);
        _reportsRepository
            .TryUpdateStatusAsync(Arg.Any<long>(), Arg.Any<ReportStatus>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var closed = await _sut.CloseOpenReportsAsync(
            TestUser, chat: null, TestExecutor, "Ban", excludeReportId: null);

        Assert.That(closed, Is.Zero, "an admin who won the race keeps ownership of the row");
    }

    [Test]
    public async Task CloseOpenReportsAsync_WithChat_ScopesTheLookup()
    {
        _reportsRepository
            .GetPendingForUserAsync(TestUser.Id, TestChat.Id, Arg.Any<CancellationToken>())
            .Returns([]);

        await _sut.CloseOpenReportsAsync(
            TestUser, TestChat, TestExecutor, "Kick", excludeReportId: null);

        await _reportsRepository.Received(1)
            .GetPendingForUserAsync(TestUser.Id, TestChat.Id, Arg.Any<CancellationToken>());
    }
}
