using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using TelegramGroupsAdmin.Telegram.Services.ReportActions;
using ModerationResult = TelegramGroupsAdmin.Telegram.Services.Moderation.ModerationResult;
using ReportStatus = TelegramGroupsAdmin.Core.Models.ReportStatus;

namespace TelegramGroupsAdmin.UnitTests.Services.ReportActions;

[TestFixture]
public class ImpersonationHandlerTests
{
    private const long TestAlertId = 500L;
    private const long TestSuspectedUserId = 100L;
    private const long TestTargetUserId = 200L;
    private const long TestChatId = -100999888777L;
    private static readonly Actor TestExecutor = Actor.FromWebUser("admin-id", "admin@test.com");

    private IReportsRepository _mockReportsRepo = null!;
    private IBotModerationService _mockModerationService = null!;
    private IReportCallbackContextRepository _mockCallbackContextRepo = null!;

    private ImpersonationHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _mockReportsRepo = Substitute.For<IReportsRepository>();
        _mockModerationService = Substitute.For<IBotModerationService>();
        _mockCallbackContextRepo = Substitute.For<IReportCallbackContextRepository>();

        _mockReportsRepo.TryUpdateStatusAsync(
                Arg.Any<long>(), Arg.Any<ReportStatus>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        _handler = new ImpersonationHandler(
            _mockReportsRepo,
            _mockModerationService,
            _mockCallbackContextRepo,
            NullLogger<ImpersonationHandler>.Instance);
    }

    #region ConfirmAsync Tests

    [Test]
    public async Task ConfirmAsync_Success_BansUserAndReturnsSuccess()
    {
        var alert = CreateTestAlert();
        _mockReportsRepo.GetImpersonationAlertAsync(TestAlertId, Arg.Any<CancellationToken>())
            .Returns(alert);

        _mockModerationService.BanUserAsync(
                Arg.Any<BanIntent>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true, ChatsAffected = 3 });

        var result = await _handler.ConfirmAsync(TestAlertId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Message, Does.Contain("3 chat(s)"));
        Assert.That(result.ActionName, Is.EqualTo("Confirm"));
    }

    [Test]
    public async Task ConfirmAsync_Success_PassesChatInBanIntent()
    {
        var alert = CreateTestAlert();
        _mockReportsRepo.GetImpersonationAlertAsync(TestAlertId, Arg.Any<CancellationToken>())
            .Returns(alert);

        _mockModerationService.BanUserAsync(
                Arg.Any<BanIntent>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true, ChatsAffected = 1 });

        await _handler.ConfirmAsync(TestAlertId, TestExecutor, CancellationToken.None);

        await _mockModerationService.Received(1).BanUserAsync(
            Arg.Is<BanIntent>(i =>
                i.User.Id == TestSuspectedUserId &&
                i.Chat != null && i.Chat.Id == TestChatId &&
                i.Reason.Contains("impersonating")),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ConfirmAsync_AlertNotFound_ReturnsFailure()
    {
        _mockReportsRepo.GetImpersonationAlertAsync(TestAlertId, Arg.Any<CancellationToken>())
            .Returns((ImpersonationAlertRecord?)null);

        var result = await _handler.ConfirmAsync(TestAlertId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("not found"));
    }

    [Test]
    public async Task ConfirmAsync_AlreadyHandled_ReturnsFailureWithAttribution()
    {
        var alert = CreateTestAlert(reviewed: true);
        _mockReportsRepo.GetImpersonationAlertAsync(TestAlertId, Arg.Any<CancellationToken>())
            .Returns(alert);

        var result = await _handler.ConfirmAsync(TestAlertId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("Already handled"));
        Assert.That(result.Message, Does.Contain("other@test.com"));
    }

    [Test]
    public async Task ConfirmAsync_ModerationFails_ReturnsFailure()
    {
        var alert = CreateTestAlert();
        _mockReportsRepo.GetImpersonationAlertAsync(TestAlertId, Arg.Any<CancellationToken>())
            .Returns(alert);

        _mockModerationService.BanUserAsync(
                Arg.Any<BanIntent>(), Arg.Any<CancellationToken>())
            .Returns(ModerationResult.Failed("Admin user"));

        var result = await _handler.ConfirmAsync(TestAlertId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("Admin user"));
    }

    [Test]
    public async Task ConfirmAsync_Success_CleansUpCallbackContext()
    {
        var alert = CreateTestAlert();
        _mockReportsRepo.GetImpersonationAlertAsync(TestAlertId, Arg.Any<CancellationToken>())
            .Returns(alert);
        _mockModerationService.BanUserAsync(
                Arg.Any<BanIntent>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true, ChatsAffected = 1 });

        await _handler.ConfirmAsync(TestAlertId, TestExecutor, CancellationToken.None);

        await _mockCallbackContextRepo.Received(1)
            .DeleteByReportIdAsync(TestAlertId, Arg.Any<CancellationToken>());
    }

    #endregion

    #region DismissAsync Tests

    [Test]
    public async Task DismissAsync_Success_ReturnsSuccess()
    {
        var alert = CreateTestAlert();
        _mockReportsRepo.GetImpersonationAlertAsync(TestAlertId, Arg.Any<CancellationToken>())
            .Returns(alert);

        var result = await _handler.DismissAsync(TestAlertId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.ActionName, Is.EqualTo("Dismiss"));
    }

    [Test]
    public async Task DismissAsync_UsesDismissedStatus()
    {
        var alert = CreateTestAlert();
        _mockReportsRepo.GetImpersonationAlertAsync(TestAlertId, Arg.Any<CancellationToken>())
            .Returns(alert);

        await _handler.DismissAsync(TestAlertId, TestExecutor, CancellationToken.None);

        await _mockReportsRepo.Received(1).TryUpdateStatusAsync(
            TestAlertId, ReportStatus.Dismissed, Arg.Any<string>(),
            "dismiss", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DismissAsync_AlreadyHandled_ReturnsFailure()
    {
        var alert = CreateTestAlert(reviewed: true);
        _mockReportsRepo.GetImpersonationAlertAsync(TestAlertId, Arg.Any<CancellationToken>())
            .Returns(alert);

        var result = await _handler.DismissAsync(TestAlertId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.False);
    }

    #endregion

    #region TrustAsync Tests

    [Test]
    public async Task TrustAsync_Success_TrustsUserAndReturnsSuccess()
    {
        var alert = CreateTestAlert();
        _mockReportsRepo.GetImpersonationAlertAsync(TestAlertId, Arg.Any<CancellationToken>())
            .Returns(alert);

        _mockModerationService.TrustUserAsync(
                Arg.Any<TrustIntent>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true });

        var result = await _handler.TrustAsync(TestAlertId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.ActionName, Is.EqualTo("Trust"));
        Assert.That(result.Message, Does.Contain("trusted"));
    }

    [Test]
    public async Task TrustAsync_UsesDismissedStatus()
    {
        var alert = CreateTestAlert();
        _mockReportsRepo.GetImpersonationAlertAsync(TestAlertId, Arg.Any<CancellationToken>())
            .Returns(alert);
        _mockModerationService.TrustUserAsync(
                Arg.Any<TrustIntent>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true });

        await _handler.TrustAsync(TestAlertId, TestExecutor, CancellationToken.None);

        await _mockReportsRepo.Received(1).TryUpdateStatusAsync(
            TestAlertId, ReportStatus.Dismissed, Arg.Any<string>(),
            "trust", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TrustAsync_ModerationFails_ReturnsFailure()
    {
        var alert = CreateTestAlert();
        _mockReportsRepo.GetImpersonationAlertAsync(TestAlertId, Arg.Any<CancellationToken>())
            .Returns(alert);

        _mockModerationService.TrustUserAsync(
                Arg.Any<TrustIntent>(), Arg.Any<CancellationToken>())
            .Returns(ModerationResult.Failed("Cannot trust"));

        var result = await _handler.TrustAsync(TestAlertId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("Cannot trust"));
    }

    #endregion

    #region Helper Methods

    private static ImpersonationAlertRecord CreateTestAlert(bool reviewed = false)
    {
        return new ImpersonationAlertRecord
        {
            Id = TestAlertId,
            SuspectedUser = new UserIdentity(TestSuspectedUserId, "Suspect", null, "suspect"),
            TargetUser = new UserIdentity(TestTargetUserId, "Target", null, "target"),
            Chat = new ChatIdentity(TestChatId, "Test Chat"),
            TotalScore = 80,
            DetectedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            ReviewedAt = reviewed ? DateTimeOffset.UtcNow.AddMinutes(-5) : null,
            ReviewedByEmail = reviewed ? "other@test.com" : null,
            Verdict = reviewed ? ImpersonationVerdict.ConfirmedScam : null
        };
    }

    #endregion
}
