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
        Assert.That(result.IsAlreadyHandled, Is.True);
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
        Assert.That(result.IsAlreadyHandled, Is.True);
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
    public async Task AllowAsync_Admitted_DeletesStrandedWelcomeMessage()
    {
        // Regression coverage for I3: TryAdmitUserAsync restores permissions but never deletes
        // the "under admin review" teaser — that was the whole point of this branch, and Allow
        // is the most common outcome of a profile scan review.
        var alert = CreateTestAlert();
        _mockReportsRepo.GetProfileScanAlertAsync(TestAlertId, Arg.Any<CancellationToken>())
            .Returns(alert);
        var welcomeResponse = new WelcomeResponse(
            Id: 1, ChatId: TestChatId, UserId: TestUserId,
            Username: "testuser", WelcomeMessageId: 777,
            Response: WelcomeResponseType.Pending,
            RespondedAt: DateTimeOffset.UtcNow,
            DmSent: false, DmFallback: false,
            CreatedAt: DateTimeOffset.UtcNow,
            TimeoutJobId: null);
        _mockWelcomeRepo.GetByUserAndChatAsync(TestUserId, TestChatId, Arg.Any<CancellationToken>())
            .Returns(welcomeResponse);
        _mockAdmissionHandler.TryAdmitUserAsync(
                Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<Actor>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(AdmissionResult.Admitted);

        await _handler.AllowAsync(TestAlertId, TestExecutor, CancellationToken.None);

        await _mockModerationService.Received(1).DeleteMessageAsync(
            Arg.Is<DeleteMessageIntent>(i => i!.MessageId == 777 && i.Chat.Id == TestChatId),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AllowAsync_StillWaiting_DoesNotDeleteWelcomeMessage()
    {
        // Must be gated on Admitted — on StillWaiting the response is still Pending and the
        // user still needs the buttons on the teaser message.
        var alert = CreateTestAlert();
        _mockReportsRepo.GetProfileScanAlertAsync(TestAlertId, Arg.Any<CancellationToken>())
            .Returns(alert);
        var welcomeResponse = new WelcomeResponse(
            Id: 1, ChatId: TestChatId, UserId: TestUserId,
            Username: "testuser", WelcomeMessageId: 777,
            Response: WelcomeResponseType.Pending,
            RespondedAt: DateTimeOffset.UtcNow,
            DmSent: false, DmFallback: false,
            CreatedAt: DateTimeOffset.UtcNow,
            TimeoutJobId: null);
        _mockWelcomeRepo.GetByUserAndChatAsync(TestUserId, TestChatId, Arg.Any<CancellationToken>())
            .Returns(welcomeResponse);
        _mockAdmissionHandler.TryAdmitUserAsync(
                Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<Actor>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(AdmissionResult.StillWaiting);

        await _handler.AllowAsync(TestAlertId, TestExecutor, CancellationToken.None);

        await _mockModerationService.DidNotReceive().DeleteMessageAsync(
            Arg.Any<DeleteMessageIntent>(), Arg.Any<CancellationToken>());
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
    public async Task AllowAsync_Success_AutoClosesSiblingAlerts()
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

        await _handler.AllowAsync(TestAlertId, TestExecutor, CancellationToken.None);

        // Sibling alert auto-closed
        await _mockReportsRepo.Received(1).TryUpdateStatusAsync(
            301L, ReportStatus.Reviewed, Arg.Any<string>(),
            "Auto-Allow", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AllowAsync_AlreadyHandled_ReturnsFailure()
    {
        var alert = CreateTestAlert(reviewed: true);
        _mockReportsRepo.GetProfileScanAlertAsync(TestAlertId, Arg.Any<CancellationToken>())
            .Returns(alert);

        var result = await _handler.AllowAsync(TestAlertId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.IsAlreadyHandled, Is.True);
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

    #region Race Condition Tests

    [Test]
    public async Task BanAsync_RaceCondition_TryUpdateFails_ReturnsFailureWithAttribution()
    {
        var alert = CreateTestAlert();
        _mockModerationService.BanUserAsync(Arg.Any<BanIntent>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true, ChatsAffected = 1 });

        _mockReportsRepo.TryUpdateStatusAsync(
                Arg.Any<long>(), Arg.Any<ReportStatus>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var handled = CreateTestAlert(reviewed: true);
        _mockReportsRepo.GetProfileScanAlertAsync(TestAlertId, Arg.Any<CancellationToken>())
            .Returns(alert, handled);

        var result = await _handler.BanAsync(TestAlertId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("Already handled"));
        Assert.That(result.IsAlreadyHandled, Is.True);
    }

    [Test]
    public async Task KickAsync_RaceCondition_TryUpdateFails_ReturnsFailureWithAttribution()
    {
        var alert = CreateTestAlert();
        _mockModerationService.KickUserFromChatAsync(Arg.Any<KickIntent>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true });

        _mockReportsRepo.TryUpdateStatusAsync(
                Arg.Any<long>(), Arg.Any<ReportStatus>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var handled = CreateTestAlert(reviewed: true);
        _mockReportsRepo.GetProfileScanAlertAsync(TestAlertId, Arg.Any<CancellationToken>())
            .Returns(alert, handled);

        var result = await _handler.KickAsync(TestAlertId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("Already handled"));
        Assert.That(result.IsAlreadyHandled, Is.True);
    }

    [Test]
    public async Task AllowAsync_RaceCondition_TryUpdateFails_ReturnsFailureWithAttribution()
    {
        var alert = CreateTestAlert();
        _mockWelcomeRepo.GetByUserAndChatAsync(TestUserId, TestChatId, Arg.Any<CancellationToken>())
            .Returns((WelcomeResponse?)null);
        _mockAdmissionHandler.TryAdmitUserAsync(
                Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<Actor>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(AdmissionResult.Admitted);

        _mockReportsRepo.TryUpdateStatusAsync(
                Arg.Any<long>(), Arg.Any<ReportStatus>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var handled = CreateTestAlert(reviewed: true);
        _mockReportsRepo.GetProfileScanAlertAsync(TestAlertId, Arg.Any<CancellationToken>())
            .Returns(alert, handled);

        var result = await _handler.AllowAsync(TestAlertId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("Already handled"));
        Assert.That(result.IsAlreadyHandled, Is.True);
    }

    #endregion

    #region Cleanup Tests

    [Test]
    public async Task BanAsync_Success_DoesNotDeleteCallbackContextsByReportId()
    {
        var alert = CreateTestAlert();
        _mockReportsRepo.GetProfileScanAlertAsync(TestAlertId, Arg.Any<CancellationToken>())
            .Returns(alert);
        _mockModerationService.BanUserAsync(Arg.Any<BanIntent>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true, ChatsAffected = 1 });

        var result = await _handler.BanAsync(TestAlertId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        await _mockCallbackContextRepo.DidNotReceive()
            .DeleteByReportIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task KickAsync_Success_DoesNotDeleteCallbackContextsByReportId()
    {
        var alert = CreateTestAlert();
        _mockReportsRepo.GetProfileScanAlertAsync(TestAlertId, Arg.Any<CancellationToken>())
            .Returns(alert);
        _mockModerationService.KickUserFromChatAsync(Arg.Any<KickIntent>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true });

        var result = await _handler.KickAsync(TestAlertId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        await _mockCallbackContextRepo.DidNotReceive()
            .DeleteByReportIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AllowAsync_Success_DoesNotDeleteCallbackContextsByReportId()
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
        await _mockCallbackContextRepo.DidNotReceive()
            .DeleteByReportIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Orchestrator Ownership Tests

    [Test]
    public async Task BanAsync_PassesOriginReportIdToOrchestrator()
    {
        _mockReportsRepo.GetProfileScanAlertAsync(TestAlertId, Arg.Any<CancellationToken>())
            .Returns(CreateTestAlert());
        _mockModerationService.BanUserAsync(Arg.Any<BanIntent>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true, ChatsAffected = 1 });

        await _handler.BanAsync(TestAlertId, TestExecutor, CancellationToken.None);

        await _mockModerationService.Received(1).BanUserAsync(
            Arg.Is<BanIntent>(i => i!.OriginReportId == TestAlertId),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task KickAsync_PassesOriginReportIdToOrchestrator()
    {
        _mockReportsRepo.GetProfileScanAlertAsync(TestAlertId, Arg.Any<CancellationToken>())
            .Returns(CreateTestAlert());
        _mockModerationService.KickUserFromChatAsync(Arg.Any<KickIntent>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true, ChatsAffected = 1 });

        await _handler.KickAsync(TestAlertId, TestExecutor, CancellationToken.None);

        await _mockModerationService.Received(1).KickUserFromChatAsync(
            Arg.Is<KickIntent>(i => i!.OriginReportId == TestAlertId),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task BanAsync_DoesNotCloseSiblingAlertsItself()
    {
        _mockReportsRepo.GetProfileScanAlertAsync(TestAlertId, Arg.Any<CancellationToken>())
            .Returns(CreateTestAlert());
        _mockModerationService.BanUserAsync(Arg.Any<BanIntent>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true, ChatsAffected = 1 });

        await _handler.BanAsync(TestAlertId, TestExecutor, CancellationToken.None);

        await _mockReportsRepo.DidNotReceive().GetPendingProfileScanAlertsForUserAsync(
            Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AllowAsync_StillClosesSiblingProfileScanAlerts()
    {
        _mockReportsRepo.GetProfileScanAlertAsync(TestAlertId, Arg.Any<CancellationToken>())
            .Returns(CreateTestAlert());
        _mockWelcomeRepo.GetByUserAndChatAsync(TestUserId, TestChatId, Arg.Any<CancellationToken>())
            .Returns((WelcomeResponse?)null);
        _mockAdmissionHandler.TryAdmitUserAsync(
                Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<Actor>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(AdmissionResult.Admitted);

        await _handler.AllowAsync(TestAlertId, TestExecutor, CancellationToken.None);

        await _mockReportsRepo.Received(1).GetPendingProfileScanAlertsForUserAsync(
            TestUserId, Arg.Any<CancellationToken>());
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
