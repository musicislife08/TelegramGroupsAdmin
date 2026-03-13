using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using TelegramGroupsAdmin.Telegram.Services.ReportActions;
using TelegramGroupsAdmin.Telegram.Services.Welcome;
using ModerationResult = TelegramGroupsAdmin.Telegram.Services.Moderation.ModerationResult;
using ReportStatus = TelegramGroupsAdmin.Core.Models.ReportStatus;

namespace TelegramGroupsAdmin.UnitTests.Services.ReportActions;

[TestFixture]
public class ProfileScanHandlerTests
{
    private const long TestAlertId = 300L;
    private const long TestUserId = 400L;
    private const long TestChatId = -100111222333L;
    private static readonly Actor TestExecutor = Actor.FromWebUser("admin-id", "admin@test.com");

    private IReportsRepository _mockReportsRepo = null!;
    private IBotModerationService _mockModerationService = null!;
    private IWelcomeResponsesRepository _mockWelcomeRepo = null!;
    private IWelcomeAdmissionHandler _mockAdmissionHandler = null!;
    private IReportCallbackContextRepository _mockCallbackContextRepo = null!;

    private ProfileScanHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _mockReportsRepo = Substitute.For<IReportsRepository>();
        _mockModerationService = Substitute.For<IBotModerationService>();
        _mockWelcomeRepo = Substitute.For<IWelcomeResponsesRepository>();
        _mockAdmissionHandler = Substitute.For<IWelcomeAdmissionHandler>();
        _mockCallbackContextRepo = Substitute.For<IReportCallbackContextRepository>();

        _mockReportsRepo.TryUpdateStatusAsync(
                Arg.Any<long>(), Arg.Any<ReportStatus>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // Default: no sibling alerts
        _mockReportsRepo.GetPendingProfileScanAlertsForUserAsync(
                Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(new List<ProfileScanAlertRecord>());

        _handler = new ProfileScanHandler(
            _mockReportsRepo,
            _mockModerationService,
            _mockWelcomeRepo,
            _mockAdmissionHandler,
            _mockCallbackContextRepo,
            NullLogger<ProfileScanHandler>.Instance);
    }

    #region BanAsync Tests

    [Test]
    public async Task BanAsync_Success_BansUserAndReturnsSuccess()
    {
        var alert = CreateTestAlert();
        _mockReportsRepo.GetProfileScanAlertAsync(TestAlertId, Arg.Any<CancellationToken>())
            .Returns(alert);

        _mockModerationService.BanUserAsync(
                Arg.Any<BanIntent>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true, ChatsAffected = 2 });

        var result = await _handler.BanAsync(TestAlertId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Message, Does.Contain("2 chat(s)"));
        Assert.That(result.ActionName, Is.EqualTo("Ban"));
    }

    [Test]
    public async Task BanAsync_AlertNotFound_ReturnsFailure()
    {
        _mockReportsRepo.GetProfileScanAlertAsync(TestAlertId, Arg.Any<CancellationToken>())
            .Returns((ProfileScanAlertRecord?)null);

        var result = await _handler.BanAsync(TestAlertId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("not found"));
    }

    [Test]
    public async Task BanAsync_AlreadyHandled_ReturnsFailureWithAttribution()
    {
        var alert = CreateTestAlert(reviewed: true);
        _mockReportsRepo.GetProfileScanAlertAsync(TestAlertId, Arg.Any<CancellationToken>())
            .Returns(alert);

        var result = await _handler.BanAsync(TestAlertId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("Already handled"));
    }

    [Test]
    public async Task BanAsync_ModerationFails_ReturnsFailure()
    {
        var alert = CreateTestAlert();
        _mockReportsRepo.GetProfileScanAlertAsync(TestAlertId, Arg.Any<CancellationToken>())
            .Returns(alert);

        _mockModerationService.BanUserAsync(
                Arg.Any<BanIntent>(), Arg.Any<CancellationToken>())
            .Returns(ModerationResult.Failed("Cannot ban admin"));

        var result = await _handler.BanAsync(TestAlertId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("Cannot ban admin"));
    }

    [Test]
    public async Task BanAsync_Success_AutoClosesSiblingAlerts()
    {
        var alert = CreateTestAlert();
        _mockReportsRepo.GetProfileScanAlertAsync(TestAlertId, Arg.Any<CancellationToken>())
            .Returns(alert);
        _mockModerationService.BanUserAsync(
                Arg.Any<BanIntent>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true, ChatsAffected = 1 });

        var siblingAlert = new ProfileScanAlertRecord
        {
            Id = 301L,
            User = new UserIdentity(TestUserId, "Test", null, "testuser"),
            Chat = new ChatIdentity(-100999L, "Other Chat"),
            Score = 3.5m
        };
        _mockReportsRepo.GetPendingProfileScanAlertsForUserAsync(
                TestUserId, Arg.Any<CancellationToken>())
            .Returns(new List<ProfileScanAlertRecord> { siblingAlert });

        await _handler.BanAsync(TestAlertId, TestExecutor, CancellationToken.None);

        // Sibling alert auto-closed
        await _mockReportsRepo.Received(1).TryUpdateStatusAsync(
            301L, ReportStatus.Reviewed, Arg.Any<string>(),
            "Auto-Ban", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region KickAsync Tests

    [Test]
    public async Task KickAsync_Success_KicksUserAndReturnsSuccess()
    {
        var alert = CreateTestAlert();
        _mockReportsRepo.GetProfileScanAlertAsync(TestAlertId, Arg.Any<CancellationToken>())
            .Returns(alert);

        _mockModerationService.KickUserFromChatAsync(
                Arg.Any<KickIntent>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true });

        var result = await _handler.KickAsync(TestAlertId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.ActionName, Is.EqualTo("Kick"));
    }

    [Test]
    public async Task KickAsync_GlobalAlert_SkipsKickAndReturnsSuccess()
    {
        var alert = new ProfileScanAlertRecord
        {
            Id = TestAlertId,
            User = new UserIdentity(TestUserId, "Test", null, "testuser"),
            Chat = new ChatIdentity(0, "Global"),
            Score = 3.5m
        };
        _mockReportsRepo.GetProfileScanAlertAsync(TestAlertId, Arg.Any<CancellationToken>())
            .Returns(alert);

        var result = await _handler.KickAsync(TestAlertId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Message, Does.Contain("no chat to kick from"));

        await _mockModerationService.DidNotReceive()
            .KickUserFromChatAsync(Arg.Any<KickIntent>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task KickAsync_AlreadyHandled_ReturnsFailure()
    {
        var alert = CreateTestAlert(reviewed: true);
        _mockReportsRepo.GetProfileScanAlertAsync(TestAlertId, Arg.Any<CancellationToken>())
            .Returns(alert);

        var result = await _handler.KickAsync(TestAlertId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.False);
    }

    #endregion

    #region AllowAsync Tests

    [Test]
    public async Task AllowAsync_Success_CallsTryAdmitAndReturnsSuccess()
    {
        var alert = CreateTestAlert();
        _mockReportsRepo.GetProfileScanAlertAsync(TestAlertId, Arg.Any<CancellationToken>())
            .Returns(alert);
        _mockWelcomeRepo.GetByUserAndChatAsync(TestUserId, TestChatId, Arg.Any<CancellationToken>())
            .Returns((WelcomeResponse?)null);
        _mockAdmissionHandler.TryAdmitUserAsync(
                Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<Actor>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(AdmissionResult.Admitted);

        var result = await _handler.AllowAsync(TestAlertId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.ActionName, Is.EqualTo("Allow"));
        Assert.That(result.Message, Does.Contain("permissions restored"));
    }

    [Test]
    public async Task AllowAsync_StillWaiting_ReturnsWaitingMessage()
    {
        var alert = CreateTestAlert();
        _mockReportsRepo.GetProfileScanAlertAsync(TestAlertId, Arg.Any<CancellationToken>())
            .Returns(alert);
        _mockWelcomeRepo.GetByUserAndChatAsync(TestUserId, TestChatId, Arg.Any<CancellationToken>())
            .Returns((WelcomeResponse?)null);
        _mockAdmissionHandler.TryAdmitUserAsync(
                Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<Actor>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(AdmissionResult.StillWaiting);

        var result = await _handler.AllowAsync(TestAlertId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Message, Does.Contain("awaiting welcome gate"));
    }

    [Test]
    public async Task AllowAsync_TimedOutUser_SkipsAdmissionAndDismisses()
    {
        var alert = CreateTestAlert();
        _mockReportsRepo.GetProfileScanAlertAsync(TestAlertId, Arg.Any<CancellationToken>())
            .Returns(alert);

        var welcomeResponse = new WelcomeResponse(
            Id: 1, ChatId: TestChatId, UserId: TestUserId,
            Username: "testuser", WelcomeMessageId: 100,
            Response: WelcomeResponseType.Timeout,
            RespondedAt: DateTimeOffset.UtcNow,
            DmSent: false, DmFallback: false,
            CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-10),
            TimeoutJobId: null);
        _mockWelcomeRepo.GetByUserAndChatAsync(TestUserId, TestChatId, Arg.Any<CancellationToken>())
            .Returns(welcomeResponse);

        var result = await _handler.AllowAsync(TestAlertId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Message, Does.Contain("already left"));

        // Admission should NOT be attempted
        await _mockAdmissionHandler.DidNotReceive().TryAdmitUserAsync(
            Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<Actor>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AllowAsync_AlreadyHandled_ReturnsFailure()
    {
        var alert = CreateTestAlert(reviewed: true);
        _mockReportsRepo.GetProfileScanAlertAsync(TestAlertId, Arg.Any<CancellationToken>())
            .Returns(alert);

        var result = await _handler.AllowAsync(TestAlertId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.False);
    }

    [Test]
    public async Task AllowAsync_UsesDismissedStatus()
    {
        var alert = CreateTestAlert();
        _mockReportsRepo.GetProfileScanAlertAsync(TestAlertId, Arg.Any<CancellationToken>())
            .Returns(alert);
        _mockWelcomeRepo.GetByUserAndChatAsync(TestUserId, TestChatId, Arg.Any<CancellationToken>())
            .Returns((WelcomeResponse?)null);
        _mockAdmissionHandler.TryAdmitUserAsync(
                Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<Actor>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(AdmissionResult.Admitted);

        await _handler.AllowAsync(TestAlertId, TestExecutor, CancellationToken.None);

        await _mockReportsRepo.Received(1).TryUpdateStatusAsync(
            TestAlertId, ReportStatus.Dismissed, Arg.Any<string>(),
            "allow", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Helper Methods

    private static ProfileScanAlertRecord CreateTestAlert(bool reviewed = false)
    {
        return new ProfileScanAlertRecord
        {
            Id = TestAlertId,
            User = new UserIdentity(TestUserId, "Test", null, "testuser"),
            Chat = new ChatIdentity(TestChatId, "Test Chat"),
            Score = 3.5m,
            ReviewedAt = reviewed ? DateTimeOffset.UtcNow.AddMinutes(-5) : null,
            ReviewedByEmail = reviewed ? "other@test.com" : null,
            ActionTaken = reviewed ? "ban" : null
        };
    }

    #endregion
}
