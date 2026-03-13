using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.ReportActions;
using ReportType = TelegramGroupsAdmin.Core.Models.ReportType;
using ReportCallbackContext = TelegramGroupsAdmin.Telegram.Models.ReportCallbackContext;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.BotCommands;

/// <summary>
/// Unit tests for ReportCallbackService (thin adapter).
/// Tests callback parsing, context lookup, routing to IReportActionsService,
/// DM message updates, and callback context cleanup.
/// Business logic tests are in handler-specific test files.
/// </summary>
[TestFixture]
public class ReportCallbackServiceTests
{
    private const long TestContextId = 12345L;
    private const long TestReportId = 99L;
    private const long TestChatId = -100123456789L;
    private const long TestUserId = 54321L;
    private const long TestDmChatId = 11111L;
    private const int TestDmMessageId = 42;

    private ILogger<ReportCallbackService> _mockLogger = null!;
    private IServiceScopeFactory _mockScopeFactory = null!;
    private IServiceScope _mockScope = null!;
    private IServiceProvider _mockServiceProvider = null!;
    private IReportCallbackContextRepository _mockCallbackContextRepo = null!;
    private IBotDmService _mockDmService = null!;
    private IReportActionsService _mockReportActionsService = null!;

    private ReportCallbackService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _mockLogger = Substitute.For<ILogger<ReportCallbackService>>();
        _mockScopeFactory = Substitute.For<IServiceScopeFactory>();
        _mockScope = Substitute.For<IServiceScope>();
        _mockServiceProvider = Substitute.For<IServiceProvider>();
        _mockCallbackContextRepo = Substitute.For<IReportCallbackContextRepository>();
        _mockDmService = Substitute.For<IBotDmService>();
        _mockReportActionsService = Substitute.For<IReportActionsService>();

        _mockScopeFactory.CreateScope().Returns(_mockScope);
        _mockScopeFactory.CreateAsyncScope().Returns(new AsyncServiceScope(_mockScope));
        _mockScope.ServiceProvider.Returns(_mockServiceProvider);

        _mockServiceProvider.GetService(typeof(IReportCallbackContextRepository))
            .Returns(_mockCallbackContextRepo);
        _mockServiceProvider.GetService(typeof(IBotDmService))
            .Returns(_mockDmService);

        _service = new ReportCallbackService(
            _mockLogger,
            _mockScopeFactory,
            _mockReportActionsService);
    }

    [TearDown]
    public void TearDown()
    {
        _mockScope?.Dispose();
    }

    #region CanHandle Tests

    [Test]
    public void CanHandle_ReviewPrefix_ReturnsTrue()
    {
        Assert.That(_service.CanHandle("rev:123:0"), Is.True);
    }

    [Test]
    public void CanHandle_OtherPrefix_ReturnsFalse()
    {
        Assert.That(_service.CanHandle("ban:123"), Is.False);
    }

    [Test]
    public void CanHandle_EmptyString_ReturnsFalse()
    {
        Assert.That(_service.CanHandle(""), Is.False);
    }

    #endregion

    #region Parsing Tests

    [Test]
    public async Task HandleCallbackAsync_NullData_ReturnsEarly()
    {
        var callback = CreateCallbackQuery(data: null);

        await _service.HandleCallbackAsync(callback);

        await _mockReportActionsService.DidNotReceiveWithAnyArgs()
            .HandleContentSpamAsync(default, default!, default);
    }

    [Test]
    public async Task HandleCallbackAsync_EmptyData_ReturnsEarly()
    {
        var callback = CreateCallbackQuery(data: "");

        await _service.HandleCallbackAsync(callback);

        await _mockReportActionsService.DidNotReceiveWithAnyArgs()
            .HandleContentSpamAsync(default, default!, default);
    }

    [Test]
    public async Task HandleCallbackAsync_InvalidFormat_OnePart_ReturnsEarly()
    {
        var callback = CreateCallbackQuery(data: "rev:123");

        await _service.HandleCallbackAsync(callback);

        await _mockReportActionsService.DidNotReceiveWithAnyArgs()
            .HandleContentSpamAsync(default, default!, default);
    }

    [Test]
    public async Task HandleCallbackAsync_InvalidContextId_ReturnsEarly()
    {
        var callback = CreateCallbackQuery(data: "rev:notanumber:0");

        await _service.HandleCallbackAsync(callback);

        await _mockCallbackContextRepo.DidNotReceiveWithAnyArgs()
            .GetByIdAsync(default, default);
    }

    [Test]
    public async Task HandleCallbackAsync_InvalidActionId_ReturnsEarly()
    {
        var callback = CreateCallbackQuery(data: "rev:123:notanumber");

        await _service.HandleCallbackAsync(callback);

        await _mockCallbackContextRepo.DidNotReceiveWithAnyArgs()
            .GetByIdAsync(default, default);
    }

    #endregion

    #region Context Lookup Tests

    [Test]
    public async Task HandleCallbackAsync_ContextNotFound_UpdatesMessageWithExpiredNotice()
    {
        var callback = CreateCallbackQuery(data: $"rev:{TestContextId}:0");
        _mockCallbackContextRepo.GetByIdAsync(TestContextId, Arg.Any<CancellationToken>())
            .Returns((ReportCallbackContext?)null);

        await _service.HandleCallbackAsync(callback);

        await _mockDmService.Received(1).EditDmTextAsync(
            TestDmChatId, TestDmMessageId,
            Arg.Is<string>(s => s.Contains("expired")),
            replyMarkup: null,
            cancellationToken: Arg.Any<CancellationToken>());

        // No action service calls
        await _mockReportActionsService.DidNotReceiveWithAnyArgs()
            .HandleContentSpamAsync(default, default!, default);
    }

    #endregion

    #region Content Report Routing Tests

    [Test]
    public async Task HandleCallbackAsync_ContentSpam_RoutesToHandleContentSpamAsync()
    {
        SetupContext(ReportType.ContentReport);
        _mockReportActionsService.HandleContentSpamAsync(TestReportId, Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(new ReviewActionResult(true, "Spam done", "Spam"));

        await _service.HandleCallbackAsync(CreateCallbackQuery(data: $"rev:{TestContextId}:0"));

        await _mockReportActionsService.Received(1)
            .HandleContentSpamAsync(TestReportId, Arg.Any<Actor>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleCallbackAsync_ContentBan_RoutesToHandleContentBanAsync()
    {
        SetupContext(ReportType.ContentReport);
        _mockReportActionsService.HandleContentBanAsync(TestReportId, Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(new ReviewActionResult(true, "Banned", "Ban"));

        await _service.HandleCallbackAsync(CreateCallbackQuery(data: $"rev:{TestContextId}:1"));

        await _mockReportActionsService.Received(1)
            .HandleContentBanAsync(TestReportId, Arg.Any<Actor>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleCallbackAsync_ContentWarn_RoutesToHandleContentWarnAsync()
    {
        SetupContext(ReportType.ContentReport);
        _mockReportActionsService.HandleContentWarnAsync(TestReportId, Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(new ReviewActionResult(true, "Warned", "Warn"));

        await _service.HandleCallbackAsync(CreateCallbackQuery(data: $"rev:{TestContextId}:2"));

        await _mockReportActionsService.Received(1)
            .HandleContentWarnAsync(TestReportId, Arg.Any<Actor>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleCallbackAsync_ContentDismiss_RoutesToHandleContentDismissAsync()
    {
        SetupContext(ReportType.ContentReport);
        _mockReportActionsService.HandleContentDismissAsync(TestReportId, Arg.Any<Actor>(), cancellationToken: Arg.Any<CancellationToken>())
            .Returns(new ReviewActionResult(true, "Dismissed", "Dismiss"));

        await _service.HandleCallbackAsync(CreateCallbackQuery(data: $"rev:{TestContextId}:3"));

        await _mockReportActionsService.Received(1)
            .HandleContentDismissAsync(TestReportId, Arg.Any<Actor>(), cancellationToken: Arg.Any<CancellationToken>());
    }

    [TestCase(-1)]
    [TestCase(4)]
    [TestCase(99)]
    public async Task HandleCallbackAsync_ContentInvalidAction_ReturnsInvalidAction(int invalidAction)
    {
        SetupContext(ReportType.ContentReport);

        await _service.HandleCallbackAsync(CreateCallbackQuery(data: $"rev:{TestContextId}:{invalidAction}"));

        // DM should be updated with "Invalid action" message
        await _mockDmService.Received(1).EditDmTextAsync(
            TestDmChatId, TestDmMessageId,
            Arg.Is<string>(s => s.Contains("Invalid action")),
            replyMarkup: null,
            cancellationToken: Arg.Any<CancellationToken>());
    }

    #endregion

    #region Impersonation Routing Tests

    [Test]
    public async Task HandleCallbackAsync_ImpersonationConfirm_RoutesCorrectly()
    {
        SetupContext(ReportType.ImpersonationAlert);
        _mockReportActionsService.HandleImpersonationConfirmAsync(TestReportId, Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(new ReviewActionResult(true, "Confirmed"));

        await _service.HandleCallbackAsync(CreateCallbackQuery(data: $"rev:{TestContextId}:0"));

        await _mockReportActionsService.Received(1)
            .HandleImpersonationConfirmAsync(TestReportId, Arg.Any<Actor>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleCallbackAsync_ImpersonationDismiss_RoutesCorrectly()
    {
        SetupContext(ReportType.ImpersonationAlert);
        _mockReportActionsService.HandleImpersonationDismissAsync(TestReportId, Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(new ReviewActionResult(true, "Dismissed"));

        await _service.HandleCallbackAsync(CreateCallbackQuery(data: $"rev:{TestContextId}:1"));

        await _mockReportActionsService.Received(1)
            .HandleImpersonationDismissAsync(TestReportId, Arg.Any<Actor>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleCallbackAsync_ImpersonationTrust_RoutesCorrectly()
    {
        SetupContext(ReportType.ImpersonationAlert);
        _mockReportActionsService.HandleImpersonationTrustAsync(TestReportId, Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(new ReviewActionResult(true, "Trusted"));

        await _service.HandleCallbackAsync(CreateCallbackQuery(data: $"rev:{TestContextId}:2"));

        await _mockReportActionsService.Received(1)
            .HandleImpersonationTrustAsync(TestReportId, Arg.Any<Actor>(), Arg.Any<CancellationToken>());
    }

    [TestCase(-1)]
    [TestCase(3)]
    [TestCase(99)]
    public async Task HandleCallbackAsync_ImpersonationInvalidAction_ReturnsInvalidAction(int invalidAction)
    {
        SetupContext(ReportType.ImpersonationAlert);

        await _service.HandleCallbackAsync(CreateCallbackQuery(data: $"rev:{TestContextId}:{invalidAction}"));

        await _mockDmService.Received(1).EditDmTextAsync(
            TestDmChatId, TestDmMessageId,
            Arg.Is<string>(s => s.Contains("Invalid action")),
            replyMarkup: null,
            cancellationToken: Arg.Any<CancellationToken>());
    }

    #endregion

    #region Exam Routing Tests

    [Test]
    public async Task HandleCallbackAsync_ExamApprove_RoutesCorrectly()
    {
        SetupContext(ReportType.ExamFailure);
        _mockReportActionsService.HandleExamApproveAsync(TestReportId, Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(new ReviewActionResult(true, "Approved"));

        await _service.HandleCallbackAsync(CreateCallbackQuery(data: $"rev:{TestContextId}:0"));

        await _mockReportActionsService.Received(1)
            .HandleExamApproveAsync(TestReportId, Arg.Any<Actor>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleCallbackAsync_ExamDeny_RoutesCorrectly()
    {
        SetupContext(ReportType.ExamFailure);
        _mockReportActionsService.HandleExamDenyAsync(TestReportId, Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(new ReviewActionResult(true, "Denied"));

        await _service.HandleCallbackAsync(CreateCallbackQuery(data: $"rev:{TestContextId}:1"));

        await _mockReportActionsService.Received(1)
            .HandleExamDenyAsync(TestReportId, Arg.Any<Actor>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleCallbackAsync_ExamDenyAndBan_RoutesCorrectly()
    {
        SetupContext(ReportType.ExamFailure);
        _mockReportActionsService.HandleExamDenyAndBanAsync(TestReportId, Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(new ReviewActionResult(true, "Banned"));

        await _service.HandleCallbackAsync(CreateCallbackQuery(data: $"rev:{TestContextId}:2"));

        await _mockReportActionsService.Received(1)
            .HandleExamDenyAndBanAsync(TestReportId, Arg.Any<Actor>(), Arg.Any<CancellationToken>());
    }

    [TestCase(-1)]
    [TestCase(3)]
    [TestCase(99)]
    public async Task HandleCallbackAsync_ExamInvalidAction_ReturnsInvalidAction(int invalidAction)
    {
        SetupContext(ReportType.ExamFailure);

        await _service.HandleCallbackAsync(CreateCallbackQuery(data: $"rev:{TestContextId}:{invalidAction}"));

        await _mockDmService.Received(1).EditDmTextAsync(
            TestDmChatId, TestDmMessageId,
            Arg.Is<string>(s => s.Contains("Invalid action")),
            replyMarkup: null,
            cancellationToken: Arg.Any<CancellationToken>());
    }

    #endregion

    #region Profile Scan Routing Tests

    [Test]
    public async Task HandleCallbackAsync_ProfileScanAllow_RoutesCorrectly()
    {
        SetupContext(ReportType.ProfileScanAlert);
        _mockReportActionsService.HandleProfileScanAllowAsync(TestReportId, Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(new ReviewActionResult(true, "Allowed"));

        await _service.HandleCallbackAsync(CreateCallbackQuery(data: $"rev:{TestContextId}:0"));

        await _mockReportActionsService.Received(1)
            .HandleProfileScanAllowAsync(TestReportId, Arg.Any<Actor>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleCallbackAsync_ProfileScanBan_RoutesCorrectly()
    {
        SetupContext(ReportType.ProfileScanAlert);
        _mockReportActionsService.HandleProfileScanBanAsync(TestReportId, Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(new ReviewActionResult(true, "Banned"));

        await _service.HandleCallbackAsync(CreateCallbackQuery(data: $"rev:{TestContextId}:1"));

        await _mockReportActionsService.Received(1)
            .HandleProfileScanBanAsync(TestReportId, Arg.Any<Actor>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleCallbackAsync_ProfileScanKick_RoutesCorrectly()
    {
        SetupContext(ReportType.ProfileScanAlert);
        _mockReportActionsService.HandleProfileScanKickAsync(TestReportId, Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(new ReviewActionResult(true, "Kicked"));

        await _service.HandleCallbackAsync(CreateCallbackQuery(data: $"rev:{TestContextId}:2"));

        await _mockReportActionsService.Received(1)
            .HandleProfileScanKickAsync(TestReportId, Arg.Any<Actor>(), Arg.Any<CancellationToken>());
    }

    [TestCase(-1)]
    [TestCase(3)]
    [TestCase(99)]
    public async Task HandleCallbackAsync_ProfileScanInvalidAction_ReturnsInvalidAction(int invalidAction)
    {
        SetupContext(ReportType.ProfileScanAlert);

        await _service.HandleCallbackAsync(CreateCallbackQuery(data: $"rev:{TestContextId}:{invalidAction}"));

        await _mockDmService.Received(1).EditDmTextAsync(
            TestDmChatId, TestDmMessageId,
            Arg.Is<string>(s => s.Contains("Invalid action")),
            replyMarkup: null,
            cancellationToken: Arg.Any<CancellationToken>());
    }

    #endregion

    #region DM Message Update Tests

    [Test]
    public async Task HandleCallbackAsync_TextMessage_UpdatesTextWithResult()
    {
        SetupContext(ReportType.ContentReport);
        _mockReportActionsService.HandleContentSpamAsync(TestReportId, Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(new ReviewActionResult(true, "Spam confirmed"));

        var callback = CreateCallbackQuery(
            data: $"rev:{TestContextId}:0",
            messageText: "Original report text");

        await _service.HandleCallbackAsync(callback);

        await _mockDmService.Received(1).EditDmTextAsync(
            TestDmChatId, TestDmMessageId,
            Arg.Is<string>(s => s.Contains("Original report text") && s.Contains("Spam confirmed")),
            replyMarkup: null,
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleCallbackAsync_PhotoMessage_UpdatesCaptionWithResult()
    {
        SetupContext(ReportType.ContentReport);
        _mockReportActionsService.HandleContentBanAsync(TestReportId, Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(new ReviewActionResult(true, "User banned"));

        var callback = CreateCallbackQuery(
            data: $"rev:{TestContextId}:1",
            messageCaption: "Photo caption",
            hasPhoto: true);

        await _service.HandleCallbackAsync(callback);

        await _mockDmService.Received(1).EditDmCaptionAsync(
            TestDmChatId, TestDmMessageId,
            Arg.Is<string>(s => s.Contains("Photo caption") && s.Contains("User banned")),
            replyMarkup: null,
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleCallbackAsync_VideoMessage_UpdatesCaptionWithResult()
    {
        SetupContext(ReportType.ContentReport);
        _mockReportActionsService.HandleContentBanAsync(TestReportId, Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(new ReviewActionResult(true, "User banned"));

        var callback = CreateCallbackQuery(
            data: $"rev:{TestContextId}:1",
            messageCaption: "Video caption",
            hasVideo: true);

        await _service.HandleCallbackAsync(callback);

        await _mockDmService.Received(1).EditDmCaptionAsync(
            TestDmChatId, TestDmMessageId,
            Arg.Is<string>(s => s.Contains("Video caption") && s.Contains("User banned")),
            replyMarkup: null,
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleCallbackAsync_NullMessage_SkipsMessageUpdate()
    {
        SetupContext(ReportType.ContentReport);
        _mockReportActionsService.HandleContentSpamAsync(TestReportId, Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(new ReviewActionResult(true, "Done"));

        var callback = new CallbackQuery
        {
            Data = $"rev:{TestContextId}:0",
            From = new User { Id = 99999, FirstName = "Admin" },
            Message = null
        };

        await _service.HandleCallbackAsync(callback);

        // Action should still execute
        await _mockReportActionsService.Received(1)
            .HandleContentSpamAsync(TestReportId, Arg.Any<Actor>(), Arg.Any<CancellationToken>());

        // But no DM update
        await _mockDmService.DidNotReceiveWithAnyArgs()
            .EditDmTextAsync(default, default, default!, default, default);
    }

    [Test]
    public async Task HandleCallbackAsync_DmUpdateFails_DoesNotThrow()
    {
        SetupContext(ReportType.ContentReport);
        _mockReportActionsService.HandleContentSpamAsync(TestReportId, Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(new ReviewActionResult(true, "Done"));

        _mockDmService.EditDmTextAsync(
                Arg.Any<long>(), Arg.Any<int>(), Arg.Any<string>(),
                Arg.Any<InlineKeyboardMarkup?>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("DM edit failed"));

        await _service.HandleCallbackAsync(CreateCallbackQuery(data: $"rev:{TestContextId}:0"));

        // Should complete without throwing
        await _mockCallbackContextRepo.Received(1)
            .DeleteAsync(TestContextId, Arg.Any<CancellationToken>());
    }

    #endregion

    #region Context Cleanup Tests

    [Test]
    public async Task HandleCallbackAsync_Success_DeletesCallbackContext()
    {
        SetupContext(ReportType.ContentReport);
        _mockReportActionsService.HandleContentSpamAsync(TestReportId, Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(new ReviewActionResult(true, "Done"));

        await _service.HandleCallbackAsync(CreateCallbackQuery(data: $"rev:{TestContextId}:0"));

        await _mockCallbackContextRepo.Received(1)
            .DeleteAsync(TestContextId, Arg.Any<CancellationToken>());
    }

    #endregion

    #region Actor Creation Tests

    [Test]
    public async Task HandleCallbackAsync_CreatesActorFromTelegramUser()
    {
        SetupContext(ReportType.ContentReport);
        _mockReportActionsService.HandleContentSpamAsync(TestReportId, Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(new ReviewActionResult(true, "Done"));

        var callback = CreateCallbackQuery(data: $"rev:{TestContextId}:0");

        await _service.HandleCallbackAsync(callback);

        await _mockReportActionsService.Received(1).HandleContentSpamAsync(
            TestReportId,
            Arg.Is<Actor>(a => a.TelegramUserId == 99999),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Helper Methods

    private void SetupContext(ReportType reportType)
    {
        var context = new ReportCallbackContext(
            Id: TestContextId,
            ReportId: TestReportId,
            ReportType: reportType,
            ChatId: TestChatId,
            UserId: TestUserId,
            CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-5));

        _mockCallbackContextRepo.GetByIdAsync(TestContextId, Arg.Any<CancellationToken>())
            .Returns(context);
    }

    private static CallbackQuery CreateCallbackQuery(
        string? data = null,
        string? messageText = null,
        string? messageCaption = null,
        bool hasPhoto = false,
        bool hasVideo = false)
    {
        Message? message = null;
        if (messageText != null || messageCaption != null || hasPhoto || hasVideo)
        {
            message = new Message
            {
                Chat = new Chat { Id = TestDmChatId },
                Id = TestDmMessageId,
                Text = messageText,
                Caption = messageCaption,
                Photo = hasPhoto ? [new PhotoSize { FileId = "photo1", FileUniqueId = "u1", Width = 100, Height = 100 }] : null,
                Video = hasVideo ? new Video { FileId = "video1", FileUniqueId = "u2", Width = 100, Height = 100, Duration = 10 } : null
            };
        }
        else
        {
            // Default text message
            message = new Message
            {
                Chat = new Chat { Id = TestDmChatId },
                Id = TestDmMessageId,
                Text = "Report notification"
            };
        }

        return new CallbackQuery
        {
            Data = data,
            From = new User { Id = 99999, FirstName = "Admin", Username = "admin" },
            Message = message
        };
    }

    #endregion
}
