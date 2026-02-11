using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using ReportBase = TelegramGroupsAdmin.Core.Models.ReportBase;
using ReportStatus = TelegramGroupsAdmin.Core.Models.ReportStatus;
using ReportType = TelegramGroupsAdmin.Core.Models.ReportType;
using TelegramUser = TelegramGroupsAdmin.Telegram.Models.TelegramUser;
using ReportCallbackContext = TelegramGroupsAdmin.Telegram.Models.ReportCallbackContext;
// Fully qualify ModerationResult to avoid ambiguity with Models.ModerationResult
using ModerationResult = TelegramGroupsAdmin.Telegram.Services.Moderation.ModerationResult;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.BotCommands;

/// <summary>
/// Unit tests for ReportCallbackService.
/// Tests callback parsing, context lookup, action execution, and cleanup.
/// Uses IBotModerationService for moderation, IBotDmService for DMs, IBotMessageService for messages.
/// </summary>
[TestFixture]
public class ReportCallbackServiceTests
{
    private const long TestContextId = 12345L;
    private const long TestReportId = 99L;
    private const long TestChatId = -100123456789L;
    private const long TestUserId = 54321L;
    private const int TestMessageId = 42;

    private ILogger<ReportCallbackService> _mockLogger = null!;
    private IServiceScopeFactory _mockScopeFactory = null!;
    private IServiceScope _mockScope = null!;
    private IServiceProvider _mockServiceProvider = null!;

    // Scoped services resolved from the mock scope
    private IReportCallbackContextRepository _mockCallbackContextRepo = null!;
    private IReportsRepository _mockReportsRepo = null!;
    private ITelegramUserRepository _mockUserRepo = null!;
    private IBotModerationService _mockModerationService = null!;
    private IExamFlowService _mockExamFlowService = null!;
    private IBotDmService _mockDmService = null!;
    private IBotMessageService _mockMessageService = null!;
    private IManagedChatsRepository _mockManagedChatsRepo = null!;

    private ReportCallbackService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _mockLogger = Substitute.For<ILogger<ReportCallbackService>>();
        _mockScopeFactory = Substitute.For<IServiceScopeFactory>();
        _mockScope = Substitute.For<IServiceScope>();
        _mockServiceProvider = Substitute.For<IServiceProvider>();

        // Scoped services
        _mockCallbackContextRepo = Substitute.For<IReportCallbackContextRepository>();
        _mockReportsRepo = Substitute.For<IReportsRepository>();
        _mockUserRepo = Substitute.For<ITelegramUserRepository>();
        _mockModerationService = Substitute.For<IBotModerationService>();
        _mockExamFlowService = Substitute.For<IExamFlowService>();
        _mockDmService = Substitute.For<IBotDmService>();
        _mockMessageService = Substitute.For<IBotMessageService>();
        _mockManagedChatsRepo = Substitute.For<IManagedChatsRepository>();

        // Wire up scope factory → scope → service provider
        _mockScopeFactory.CreateScope().Returns(_mockScope);
        _mockScopeFactory.CreateAsyncScope().Returns(new AsyncServiceScope(_mockScope));
        _mockScope.ServiceProvider.Returns(_mockServiceProvider);

        // Wire up service resolution
        _mockServiceProvider.GetService(typeof(IReportCallbackContextRepository))
            .Returns(_mockCallbackContextRepo);
        _mockServiceProvider.GetService(typeof(IReportsRepository))
            .Returns(_mockReportsRepo);
        _mockServiceProvider.GetService(typeof(ITelegramUserRepository))
            .Returns(_mockUserRepo);
        _mockServiceProvider.GetService(typeof(IBotModerationService))
            .Returns(_mockModerationService);
        _mockServiceProvider.GetService(typeof(IExamFlowService))
            .Returns(_mockExamFlowService);
        _mockServiceProvider.GetService(typeof(IBotDmService))
            .Returns(_mockDmService);
        _mockServiceProvider.GetService(typeof(IBotMessageService))
            .Returns(_mockMessageService);
        _mockServiceProvider.GetService(typeof(IManagedChatsRepository))
            .Returns(_mockManagedChatsRepo);

        _service = new ReportCallbackService(
            _mockLogger,
            _mockScopeFactory);
    }

    [TearDown]
    public void TearDown()
    {
        _mockScope?.Dispose();
    }

    #region CanHandle Tests

    [Test]
    [TestCase("ban_select:123:456")]
    [TestCase("ban_cancel:456")]
    [TestCase("welcome:123")]
    [TestCase("rpt:123:0")] // Legacy prefix no longer handled
    [TestCase("other:data")]
    [TestCase("")]
    public void CanHandle_NonReportPrefix_ReturnsFalse(string callbackData)
    {
        // Act
        var result = _service.CanHandle(callbackData);

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region Callback Data Parsing Tests

    [Test]
    public async Task HandleCallbackAsync_NullData_ReturnsEarly()
    {
        // Arrange
        var callbackQuery = CreateCallbackQuery(data: null);

        // Act
        await _service.HandleCallbackAsync(callbackQuery);

        // Assert - no scope created since we return early
        _mockScopeFactory.DidNotReceive().CreateScope();
    }

    [Test]
    public async Task HandleCallbackAsync_EmptyData_ReturnsEarly()
    {
        // Arrange
        var callbackQuery = CreateCallbackQuery(data: "");

        // Act
        await _service.HandleCallbackAsync(callbackQuery);

        // Assert
        _mockScopeFactory.DidNotReceive().CreateScope();
    }

    [Test]
    public async Task HandleCallbackAsync_InvalidFormat_OnePart_ReturnsEarly()
    {
        // Arrange - only 1 part after prefix (need 2: contextId:action)
        var callbackQuery = CreateCallbackQuery(data: "rpt:123");

        // Act
        await _service.HandleCallbackAsync(callbackQuery);

        // Assert - scope created but no context lookup attempted
        await _mockCallbackContextRepo.DidNotReceive()
            .GetByIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleCallbackAsync_InvalidContextId_ReturnsEarly()
    {
        // Arrange - non-numeric context ID
        var callbackQuery = CreateCallbackQuery(data: "rpt:abc:0");

        // Act
        await _service.HandleCallbackAsync(callbackQuery);

        // Assert
        await _mockCallbackContextRepo.DidNotReceive()
            .GetByIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleCallbackAsync_InvalidActionId_ReturnsEarly()
    {
        // Arrange - non-numeric action
        var callbackQuery = CreateCallbackQuery(data: "rpt:123:xyz");

        // Act
        await _service.HandleCallbackAsync(callbackQuery);

        // Assert
        await _mockCallbackContextRepo.DidNotReceive()
            .GetByIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Test]
    [TestCase(-1, Description = "Negative action value")]
    [TestCase(4, Description = "Action value exceeds Dismiss (3)")]
    [TestCase(99, Description = "Way out of range")]
    public async Task HandleCallbackAsync_ActionOutOfRange_ReturnsInvalidActionMessage(int invalidAction)
    {
        // Arrange - action validation depends on review type, so context lookup must happen first
        var callbackQuery = CreateCallbackQuery(data: $"rpt:{TestContextId}:{invalidAction}");

        var context = CreateTestContext();
        _mockCallbackContextRepo.GetByIdAsync(TestContextId, Arg.Any<CancellationToken>())
            .Returns(context);

        var report = CreateTestReport();
        _mockReportsRepo.GetByIdAsync(TestReportId, Arg.Any<CancellationToken>())
            .Returns(report);

        // Act
        await _service.HandleCallbackAsync(callbackQuery);

        // Assert - message updated to show invalid action
        await _mockDmService.Received(1).EditDmTextAsync(
            Arg.Any<long>(),
            Arg.Any<int>(),
            Arg.Is<string>(s => s.Contains("Invalid action") || s.Contains("failed")),
            replyMarkup: null,
            cancellationToken: Arg.Any<CancellationToken>());
    }

    #endregion

    #region Context Lookup Tests

    [Test]
    public async Task HandleCallbackAsync_ContextNotFound_UpdatesMessageWithExpiredNotice()
    {
        // Arrange
        var callbackQuery = CreateCallbackQuery(data: $"rpt:{TestContextId}:0");
        _mockCallbackContextRepo.GetByIdAsync(TestContextId, Arg.Any<CancellationToken>())
            .Returns((ReportCallbackContext?)null);

        // Act
        await _service.HandleCallbackAsync(callbackQuery);

        // Assert - message updated to show expired
        await _mockDmService.Received(1).EditDmTextAsync(
            Arg.Any<long>(),
            Arg.Any<int>(),
            Arg.Is<string>(s => s.Contains("expired")),
            replyMarkup: null,
            cancellationToken: Arg.Any<CancellationToken>());

        // No report lookup should happen
        await _mockReportsRepo.DidNotReceive()
            .GetByIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleCallbackAsync_ValidContext_ProceedsWithReportLookup()
    {
        // Arrange
        var callbackQuery = CreateCallbackQuery(data: $"rpt:{TestContextId}:0");
        var context = CreateTestContext();
        _mockCallbackContextRepo.GetByIdAsync(TestContextId, Arg.Any<CancellationToken>())
            .Returns(context);

        // Act
        await _service.HandleCallbackAsync(callbackQuery);

        // Assert - should look up the report
        await _mockReportsRepo.Received(1)
            .GetByIdAsync(TestReportId, Arg.Any<CancellationToken>());
    }

    #endregion

    #region Report Validation Tests

    [Test]
    public async Task HandleCallbackAsync_ReportNotFound_UpdatesMessageAndCleansUp()
    {
        // Arrange
        var callbackQuery = CreateCallbackQuery(data: $"rpt:{TestContextId}:0");
        SetupValidContext();
        _mockReportsRepo.GetByIdAsync(TestReportId, Arg.Any<CancellationToken>())
            .Returns((ReportBase?)null);

        // Act
        await _service.HandleCallbackAsync(callbackQuery);

        // Assert - message updated
        await _mockDmService.Received(1).EditDmTextAsync(
            Arg.Any<long>(),
            Arg.Any<int>(),
            Arg.Is<string>(s => s.Contains("not found")),
            replyMarkup: null,
            cancellationToken: Arg.Any<CancellationToken>());

        // Context deleted for cleanup
        await _mockCallbackContextRepo.Received(1)
            .DeleteAsync(TestContextId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleCallbackAsync_ReportAlreadyReviewed_DoesNotCallModeration()
    {
        // Arrange
        var callbackQuery = CreateCallbackQuery(data: $"rpt:{TestContextId}:0");
        SetupValidContext();
        var report = CreateTestReport(status: ReportStatus.Reviewed);
        _mockReportsRepo.GetByIdAsync(TestReportId, Arg.Any<CancellationToken>())
            .Returns(report);

        // Act
        await _service.HandleCallbackAsync(callbackQuery);

        // Assert - Moderation should NOT be called for already-reviewed reports
        await _mockModerationService.DidNotReceive()
            .BanUserAsync(Arg.Any<BanIntent>(), Arg.Any<CancellationToken>());

        // Context should still be cleaned up
        await _mockCallbackContextRepo.Received(1)
            .DeleteAsync(TestContextId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleCallbackAsync_RaceCondition_ShowsAlreadyHandledMessage()
    {
        // Arrange - report is pending, action succeeds, but status update fails (race condition)
        var callbackQuery = CreateCallbackQuery(data: $"rpt:{TestContextId}:3"); // Dismiss
        SetupValidContext();
        var pendingReport = CreateTestReport(status: ReportStatus.Pending);
        _mockReportsRepo.GetByIdAsync(TestReportId, Arg.Any<CancellationToken>())
            .Returns(pendingReport);

        // First call returns the pending report, but TryUpdate fails
        _mockReportsRepo.TryUpdateStatusAsync(
                TestReportId,
                ReportStatus.Reviewed,
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(false); // Race condition - another admin handled it

        // Second GetByIdAsync returns the already-reviewed report
        var reviewedReport = CreateTestReport(
            status: ReportStatus.Reviewed,
            reviewedBy: "OtherAdmin",
            actionTaken: "Spam",
            reviewedAt: DateTimeOffset.UtcNow);
        _mockReportsRepo.GetByIdAsync(TestReportId, Arg.Any<CancellationToken>())
            .Returns(pendingReport, reviewedReport);

        // Act
        await _service.HandleCallbackAsync(callbackQuery);

        // Assert - message shows already handled by other admin
        await _mockDmService.Received().EditDmTextAsync(
            Arg.Any<long>(),
            Arg.Any<int>(),
            Arg.Is<string>(s => s.Contains("OtherAdmin") && s.Contains("Spam")),
            replyMarkup: null,
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleCallbackAsync_ReportAlreadyReviewed_ShowsWhoHandledIt()
    {
        // Arrange - report is already reviewed when fetched
        var callbackQuery = CreateCallbackQuery(data: $"rpt:{TestContextId}:3"); // Dismiss
        SetupValidContext();

        var reviewedReport = CreateTestReport(
            status: ReportStatus.Reviewed,
            reviewedBy: "FirstAdmin",
            actionTaken: "Warn",
            reviewedAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        _mockReportsRepo.GetByIdAsync(TestReportId, Arg.Any<CancellationToken>())
            .Returns(reviewedReport);

        // Act
        await _service.HandleCallbackAsync(callbackQuery);

        // Assert - message shows who already handled it
        await _mockDmService.Received().EditDmTextAsync(
            Arg.Any<long>(),
            Arg.Any<int>(),
            Arg.Is<string>(s => s.Contains("FirstAdmin") && s.Contains("Warn")),
            replyMarkup: null,
            cancellationToken: Arg.Any<CancellationToken>());

        // Assert - context is cleaned up
        await _mockCallbackContextRepo.Received().DeleteAsync(TestContextId, Arg.Any<CancellationToken>());
    }

    #endregion

    #region Spam Action Tests

    [Test]
    public async Task HandleCallbackAsync_SpamAction_MarksAsSpamAndBansUser()
    {
        // Arrange
        var callbackQuery = CreateCallbackQuery(data: $"rpt:{TestContextId}:0"); // Spam = 0
        SetupValidContext();
        SetupPendingReview();
        SetupTargetUser();

        _mockModerationService.MarkAsSpamAndBanAsync(
                Arg.Any<SpamBanIntent>(),
                Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true, ChatsAffected = 5 });

        _mockReportsRepo.TryUpdateStatusAsync(
                Arg.Any<long>(), Arg.Any<ReportStatus>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        await _service.HandleCallbackAsync(callbackQuery);

        // Assert - MarkAsSpamAndBanAsync called (delete + ban + spam flag)
        await _mockModerationService.Received(1).MarkAsSpamAndBanAsync(
            Arg.Is<SpamBanIntent>(i =>
                i.User.Id == TestUserId &&
                i.Chat.Id == TestChatId &&
                i.MessageId == TestMessageId &&
                i.Reason.Contains("spam")),
            Arg.Any<CancellationToken>());

        // Report status updated
        await _mockReportsRepo.Received(1).TryUpdateStatusAsync(
            TestReportId,
            ReportStatus.Reviewed,
            Arg.Any<string>(),
            "Spam",
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleCallbackAsync_SpamAction_Fails_ReportsFailure()
    {
        // Arrange
        var callbackQuery = CreateCallbackQuery(data: $"rpt:{TestContextId}:0");
        SetupValidContext();
        SetupPendingReview();
        SetupTargetUser();

        _mockModerationService.MarkAsSpamAndBanAsync(
                Arg.Any<SpamBanIntent>(),
                Arg.Any<CancellationToken>())
            .Returns(ModerationResult.Failed("User is admin"));

        // Act
        await _service.HandleCallbackAsync(callbackQuery);

        // Assert - message updated with failure
        await _mockDmService.Received().EditDmTextAsync(
            Arg.Any<long>(),
            Arg.Any<int>(),
            Arg.Is<string>(s => s.Contains("failed") || s.Contains("User is admin")),
            replyMarkup: null,
            cancellationToken: Arg.Any<CancellationToken>());

        // Report status NOT updated on failure
        await _mockReportsRepo.DidNotReceive().TryUpdateStatusAsync(
            Arg.Any<long>(), Arg.Any<ReportStatus>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Warn Action Tests

    [Test]
    public async Task HandleCallbackAsync_WarnAction_WarnsUserAndUpdatesReport()
    {
        // Arrange
        var callbackQuery = CreateCallbackQuery(data: $"rpt:{TestContextId}:2"); // Warn = 2
        SetupValidContext();
        SetupPendingReview();
        SetupTargetUser();

        _mockModerationService.WarnUserAsync(
                Arg.Any<WarnIntent>(),
                Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true, WarningCount = 2 });

        _mockReportsRepo.TryUpdateStatusAsync(
                Arg.Any<long>(), Arg.Any<ReportStatus>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        await _service.HandleCallbackAsync(callbackQuery);

        // Assert
        await _mockModerationService.Received(1).WarnUserAsync(
            Arg.Is<WarnIntent>(i =>
                i.User.Id == TestUserId &&
                i.Chat.Id == TestChatId &&
                i.Reason.Contains("report")),
            Arg.Any<CancellationToken>());

        await _mockReportsRepo.Received(1).TryUpdateStatusAsync(
            TestReportId,
            ReportStatus.Reviewed,
            Arg.Any<string>(),
            "Warn",
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleCallbackAsync_WarnAction_WarnFails_ReportsFailure()
    {
        // Arrange
        var callbackQuery = CreateCallbackQuery(data: $"rpt:{TestContextId}:2");
        SetupValidContext();
        SetupPendingReview();
        SetupTargetUser();

        _mockModerationService.WarnUserAsync(
                Arg.Any<WarnIntent>(),
                Arg.Any<CancellationToken>())
            .Returns(ModerationResult.Failed("Database error"));

        // Act
        await _service.HandleCallbackAsync(callbackQuery);

        // Assert
        await _mockDmService.Received().EditDmTextAsync(
            Arg.Any<long>(),
            Arg.Any<int>(),
            Arg.Is<string>(s => s.Contains("failed") || s.Contains("Database error")),
            replyMarkup: null,
            cancellationToken: Arg.Any<CancellationToken>());
    }

    #endregion

    #region Ban Action Tests

    [Test]
    public async Task HandleCallbackAsync_BanAction_DeletesMessageAndBansUser()
    {
        // Arrange
        var callbackQuery = CreateCallbackQuery(data: $"rpt:{TestContextId}:1"); // Ban = 1
        SetupValidContext();
        SetupPendingReview();
        SetupTargetUser();

        _mockModerationService.BanUserAsync(
                Arg.Any<BanIntent>(),
                Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true, ChatsAffected = 3 });

        _mockReportsRepo.TryUpdateStatusAsync(
                Arg.Any<long>(), Arg.Any<ReportStatus>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        await _service.HandleCallbackAsync(callbackQuery);

        // Assert - message deleted first
        await _mockModerationService.Received(1).DeleteMessageAsync(
            Arg.Is<DeleteMessageIntent>(i =>
                i.User.Id == TestUserId &&
                i.Chat.Id == TestChatId &&
                i.MessageId == TestMessageId),
            Arg.Any<CancellationToken>());

        // Then ban
        await _mockModerationService.Received(1).BanUserAsync(
            Arg.Is<BanIntent>(i =>
                i.User.Id == TestUserId &&
                i.Reason.Contains("report")),
            Arg.Any<CancellationToken>());

        await _mockReportsRepo.Received(1).TryUpdateStatusAsync(
            TestReportId,
            ReportStatus.Reviewed,
            Arg.Any<string>(),
            "Ban",
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleCallbackAsync_BanAction_BanFails_ReportsFailure()
    {
        // Arrange
        var callbackQuery = CreateCallbackQuery(data: $"rpt:{TestContextId}:1");
        SetupValidContext();
        SetupPendingReview();
        SetupTargetUser();

        _mockModerationService.BanUserAsync(
                Arg.Any<BanIntent>(),
                Arg.Any<CancellationToken>())
            .Returns(ModerationResult.Failed("Rate limited"));

        // Act
        await _service.HandleCallbackAsync(callbackQuery);

        // Assert
        await _mockDmService.Received().EditDmTextAsync(
            Arg.Any<long>(),
            Arg.Any<int>(),
            Arg.Is<string>(s => s.Contains("failed") || s.Contains("Rate limited")),
            replyMarkup: null,
            cancellationToken: Arg.Any<CancellationToken>());
    }

    #endregion

    #region Dismiss Action Tests

    [Test]
    public async Task HandleCallbackAsync_DismissAction_DismissesReportWithoutModerationAction()
    {
        // Arrange
        var callbackQuery = CreateCallbackQuery(data: $"rpt:{TestContextId}:3"); // Dismiss = 3
        SetupValidContext();
        SetupPendingReview();

        _mockReportsRepo.TryUpdateStatusAsync(
                Arg.Any<long>(), Arg.Any<ReportStatus>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        await _service.HandleCallbackAsync(callbackQuery);

        // Assert - no moderation action called
        await _mockModerationService.DidNotReceive()
            .BanUserAsync(Arg.Any<BanIntent>(), Arg.Any<CancellationToken>());
        await _mockModerationService.DidNotReceive()
            .WarnUserAsync(Arg.Any<WarnIntent>(), Arg.Any<CancellationToken>());
        await _mockModerationService.DidNotReceive()
            .TempBanUserAsync(Arg.Any<TempBanIntent>(), Arg.Any<CancellationToken>());

        // Report status still updated
        await _mockReportsRepo.Received(1).TryUpdateStatusAsync(
            TestReportId,
            ReportStatus.Reviewed,
            Arg.Any<string>(),
            "Dismiss",
            Arg.Is<string>(s => s.Contains("dismissed")),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleCallbackAsync_DismissAction_SendsReplyToOriginalMessage()
    {
        // Arrange
        var callbackQuery = CreateCallbackQuery(data: $"rpt:{TestContextId}:3");
        SetupValidContext();
        SetupPendingReview();

        _mockReportsRepo.TryUpdateStatusAsync(
                Arg.Any<long>(), Arg.Any<ReportStatus>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        await _service.HandleCallbackAsync(callbackQuery);

        // Assert - sends reply to original message in chat
        await _mockMessageService.Received().SendAndSaveMessageAsync(
            TestChatId,
            Arg.Is<string>(s => s.Contains("reviewed") && s.Contains("no action")),
            Arg.Any<ParseMode?>(),
            Arg.Is<ReplyParameters>(r => r.MessageId == TestMessageId),
            Arg.Any<InlineKeyboardMarkup?>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Cleanup Tests

    [Test]
    public async Task HandleCallbackAsync_Success_DeletesCallbackContext()
    {
        // Arrange
        var callbackQuery = CreateCallbackQuery(data: $"rpt:{TestContextId}:3");
        SetupValidContext();
        SetupPendingReview();

        _mockReportsRepo.TryUpdateStatusAsync(
                Arg.Any<long>(), Arg.Any<ReportStatus>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        await _service.HandleCallbackAsync(callbackQuery);

        // Assert - callback context deleted
        await _mockCallbackContextRepo.Received(1)
            .DeleteAsync(TestContextId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleCallbackAsync_Success_DeletesReportCommandMessage()
    {
        // Arrange
        var reportCommandMessageId = 999;
        var callbackQuery = CreateCallbackQuery(data: $"rpt:{TestContextId}:3");
        SetupValidContext();
        var report = CreateTestReport(
            status: ReportStatus.Pending,
            reportCommandMessageId: reportCommandMessageId);
        _mockReportsRepo.GetByIdAsync(TestReportId, Arg.Any<CancellationToken>())
            .Returns(report);

        _mockReportsRepo.TryUpdateStatusAsync(
                Arg.Any<long>(), Arg.Any<ReportStatus>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        await _service.HandleCallbackAsync(callbackQuery);

        // Assert - /report command message deleted
        await _mockMessageService.Received().DeleteAndMarkMessageAsync(
            TestChatId,
            reportCommandMessageId,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleCallbackAsync_DeleteMessageFails_DoesNotThrow()
    {
        // Arrange - command message already deleted (should handle gracefully)
        var reportCommandMessageId = 999;
        var callbackQuery = CreateCallbackQuery(data: $"rpt:{TestContextId}:3");
        SetupValidContext();
        var report = CreateTestReport(
            status: ReportStatus.Pending,
            reportCommandMessageId: reportCommandMessageId);
        _mockReportsRepo.GetByIdAsync(TestReportId, Arg.Any<CancellationToken>())
            .Returns(report);

        _mockReportsRepo.TryUpdateStatusAsync(
                Arg.Any<long>(), Arg.Any<ReportStatus>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        _mockMessageService.DeleteAndMarkMessageAsync(TestChatId, reportCommandMessageId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Message not found"));

        // Act - should not throw
        await _service.HandleCallbackAsync(callbackQuery);

        // Assert - test passes if no exception thrown
    }

    #endregion

    #region Message Update Tests

    [Test]
    public async Task HandleCallbackAsync_TextMessage_UpdatesText()
    {
        // Arrange
        var callbackQuery = CreateCallbackQuery(data: $"rpt:{TestContextId}:3", isPhotoMessage: false);
        SetupValidContext();
        SetupPendingReview();

        _mockReportsRepo.TryUpdateStatusAsync(
                Arg.Any<long>(), Arg.Any<ReportStatus>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        await _service.HandleCallbackAsync(callbackQuery);

        // Assert - EditMessageTextAsync called (not EditMessageCaptionAsync)
        await _mockDmService.Received().EditDmTextAsync(
            Arg.Any<long>(),
            Arg.Any<int>(),
            Arg.Any<string>(),
            replyMarkup: null,
            cancellationToken: Arg.Any<CancellationToken>());

        await _mockDmService.DidNotReceive().EditDmCaptionAsync(
            Arg.Any<long>(),
            Arg.Any<int>(),
            Arg.Any<string?>(),
            Arg.Any<InlineKeyboardMarkup?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleCallbackAsync_PhotoMessage_UpdatesCaption()
    {
        // Arrange
        var callbackQuery = CreateCallbackQuery(data: $"rpt:{TestContextId}:3", isPhotoMessage: true);
        SetupValidContext();
        SetupPendingReview();

        _mockReportsRepo.TryUpdateStatusAsync(
                Arg.Any<long>(), Arg.Any<ReportStatus>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        await _service.HandleCallbackAsync(callbackQuery);

        // Assert - EditMessageCaptionAsync called (not EditMessageTextAsync)
        await _mockDmService.Received().EditDmCaptionAsync(
            Arg.Any<long>(),
            Arg.Any<int>(),
            Arg.Any<string>(),
            replyMarkup: null,
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleCallbackAsync_VideoMessage_UpdatesCaption()
    {
        // Arrange
        var callbackQuery = CreateCallbackQuery(data: $"rpt:{TestContextId}:3", isVideoMessage: true);
        SetupValidContext();
        SetupPendingReview();

        _mockReportsRepo.TryUpdateStatusAsync(
                Arg.Any<long>(), Arg.Any<ReportStatus>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        await _service.HandleCallbackAsync(callbackQuery);

        // Assert - EditMessageCaptionAsync called for videos (like photos)
        await _mockDmService.Received().EditDmCaptionAsync(
            Arg.Any<long>(),
            Arg.Any<int>(),
            Arg.Any<string>(),
            replyMarkup: null,
            cancellationToken: Arg.Any<CancellationToken>());

        // Verify text update was NOT called
        await _mockDmService.DidNotReceive().EditDmTextAsync(
            Arg.Any<long>(),
            Arg.Any<int>(),
            Arg.Any<string>(),
            Arg.Any<InlineKeyboardMarkup?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleCallbackAsync_NullMessage_ReturnsEarlyWithoutUpdating()
    {
        // Arrange - message is null (can happen if message was deleted before callback)
        var callbackQuery = CreateCallbackQuery(data: $"rpt:{TestContextId}:3", includeMessage: false);
        SetupValidContext();
        SetupPendingReview();

        _mockReportsRepo.TryUpdateStatusAsync(
                Arg.Any<long>(), Arg.Any<ReportStatus>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        await _service.HandleCallbackAsync(callbackQuery);

        // Assert - no message edit attempted since message is null
        await _mockDmService.DidNotReceive().EditDmTextAsync(
            Arg.Any<long>(),
            Arg.Any<int>(),
            Arg.Any<string>(),
            Arg.Any<InlineKeyboardMarkup?>(),
            Arg.Any<CancellationToken>());

        await _mockDmService.DidNotReceive().EditDmCaptionAsync(
            Arg.Any<long>(),
            Arg.Any<int>(),
            Arg.Any<string?>(),
            Arg.Any<InlineKeyboardMarkup?>(),
            Arg.Any<CancellationToken>());

        // The action should still complete (context deleted, report status updated)
        await _mockCallbackContextRepo.Received(1)
            .DeleteAsync(TestContextId, Arg.Any<CancellationToken>());
    }

    #endregion

    #region Exception Handling Tests

    [Test]
    public async Task HandleCallbackAsync_ActionThrows_ReturnsFailureResult()
    {
        // Arrange
        var callbackQuery = CreateCallbackQuery(data: $"rpt:{TestContextId}:0");
        SetupValidContext();
        SetupPendingReview();
        SetupTargetUser();

        _mockModerationService.MarkAsSpamAndBanAsync(
                Arg.Any<SpamBanIntent>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Unexpected error"));

        // Act
        await _service.HandleCallbackAsync(callbackQuery);

        // Assert - message updated with failure
        await _mockDmService.Received().EditDmTextAsync(
            Arg.Any<long>(),
            Arg.Any<int>(),
            Arg.Is<string>(s => s.Contains("failed") || s.Contains("Unexpected error")),
            replyMarkup: null,
            cancellationToken: Arg.Any<CancellationToken>());

        // Context still cleaned up
        await _mockCallbackContextRepo.Received(1)
            .DeleteAsync(TestContextId, Arg.Any<CancellationToken>());
    }

    #endregion

    #region Exam Review - Denial Notification Tests

    [Test]
    public async Task HandleCallbackAsync_ExamDeny_CallsExamFlowService()
    {
        // Arrange - Exam review with Deny action (1)
        var callbackQuery = CreateCallbackQuery(data: $"rpt:{TestContextId}:1"); // Deny = 1 for ExamAction
        SetupExamReviewContext();
        SetupPendingExamReview();

        _mockExamFlowService.DenyExamFailureAsync(
                Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true });

        _mockReportsRepo.TryUpdateStatusAsync(
                Arg.Any<long>(), Arg.Any<ReportStatus>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        await _service.HandleCallbackAsync(callbackQuery);

        // Assert - ExamFlowService.DenyExamFailureAsync called (handles teaser deletion, kick, DM)
        await _mockExamFlowService.Received(1).DenyExamFailureAsync(
            Arg.Is<UserIdentity>(u => u.Id == TestUserId),
            Arg.Is<ChatIdentity>(c => c.Id == TestChatId),
            Arg.Any<Actor>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleCallbackAsync_ExamDenyAndBan_CallsExamFlowService()
    {
        // Arrange - Exam review with DenyAndBan action (2)
        var callbackQuery = CreateCallbackQuery(data: $"rpt:{TestContextId}:2"); // DenyAndBan = 2 for ExamAction
        SetupExamReviewContext();
        SetupPendingExamReview();

        _mockExamFlowService.DenyAndBanExamFailureAsync(
                Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true });

        _mockReportsRepo.TryUpdateStatusAsync(
                Arg.Any<long>(), Arg.Any<ReportStatus>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        await _service.HandleCallbackAsync(callbackQuery);

        // Assert - ExamFlowService.DenyAndBanExamFailureAsync called (handles teaser deletion, global ban, DM)
        await _mockExamFlowService.Received(1).DenyAndBanExamFailureAsync(
            Arg.Is<UserIdentity>(u => u.Id == TestUserId),
            Arg.Is<ChatIdentity>(c => c.Id == TestChatId),
            Arg.Any<Actor>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleCallbackAsync_ExamDeny_Success_UpdatesReportStatus()
    {
        // Arrange - Exam denial succeeds
        var callbackQuery = CreateCallbackQuery(data: $"rpt:{TestContextId}:1"); // Deny
        SetupExamReviewContext();
        SetupPendingExamReview();

        // ExamFlowService succeeds (handles DM internally, even if it fails)
        _mockExamFlowService.DenyExamFailureAsync(
                Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true });

        _mockReportsRepo.TryUpdateStatusAsync(
                Arg.Any<long>(), Arg.Any<ReportStatus>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        await _service.HandleCallbackAsync(callbackQuery);

        // Assert - Report status updated to Reviewed
        await _mockReportsRepo.Received(1).TryUpdateStatusAsync(
            TestReportId,
            ReportStatus.Reviewed,
            Arg.Any<string>(),
            "Deny",
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleCallbackAsync_ExamDeny_PassesCorrectParametersToFlowService()
    {
        // Arrange - Verify correct userId and chatId are passed
        var callbackQuery = CreateCallbackQuery(data: $"rpt:{TestContextId}:1");
        SetupExamReviewContext();
        SetupPendingExamReview();

        _mockExamFlowService.DenyExamFailureAsync(
                Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true });

        _mockReportsRepo.TryUpdateStatusAsync(
                Arg.Any<long>(), Arg.Any<ReportStatus>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        await _service.HandleCallbackAsync(callbackQuery);

        // Assert - Correct parameters passed (flow service handles chat name lookup internally)
        await _mockExamFlowService.Received(1).DenyExamFailureAsync(
            Arg.Is<UserIdentity>(u => u.Id == TestUserId),    // User to kick
            Arg.Is<ChatIdentity>(c => c.Id == TestChatId),    // Chat they failed exam in
            Arg.Is<Actor>(a => a.TelegramUserId == 999), // Executor from CreateCallbackQuery
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleCallbackAsync_ExamDeny_FlowServiceFailure_ReturnsErrorResult()
    {
        // Arrange - ExamFlowService fails (e.g., kick failed)
        var callbackQuery = CreateCallbackQuery(data: $"rpt:{TestContextId}:1");
        SetupExamReviewContext();
        SetupPendingExamReview();

        _mockExamFlowService.DenyExamFailureAsync(
                Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = false, ErrorMessage = "Failed to kick user" });

        // Act
        await _service.HandleCallbackAsync(callbackQuery);

        // Assert - Report status NOT updated (action failed)
        await _mockReportsRepo.DidNotReceive().TryUpdateStatusAsync(
            Arg.Any<long>(), Arg.Any<ReportStatus>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleCallbackAsync_ExamDenyAndBan_FlowServiceFailure_ReturnsErrorResult()
    {
        // Arrange - ExamFlowService fails (e.g., ban failed)
        var callbackQuery = CreateCallbackQuery(data: $"rpt:{TestContextId}:2"); // DenyAndBan
        SetupExamReviewContext();
        SetupPendingExamReview();

        _mockExamFlowService.DenyAndBanExamFailureAsync(
                Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = false, ErrorMessage = "Failed to ban user" });

        // Act
        await _service.HandleCallbackAsync(callbackQuery);

        // Assert - Report status NOT updated (action failed)
        await _mockReportsRepo.DidNotReceive().TryUpdateStatusAsync(
            Arg.Any<long>(), Arg.Any<ReportStatus>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Helper Methods

    private enum TestMessageType { Text, Photo, Video }

    private static CallbackQuery CreateCallbackQuery(
        string? data,
        bool isPhotoMessage = false,
        bool isVideoMessage = false,
        bool includeMessage = true,
        long chatId = -1001234567890,
        int messageId = 100)
    {
        Message? message = null;

        if (data != null && includeMessage)
        {
            var messageType = isVideoMessage ? TestMessageType.Video
                            : isPhotoMessage ? TestMessageType.Photo
                            : TestMessageType.Text;
            message = CreateMessage(messageId, chatId, messageType);
        }

        return new CallbackQuery
        {
            Id = "callback123",
            Data = data,
            Message = message,
            From = new User { Id = 999, FirstName = "Admin", Username = "admin" }
        };
    }

    /// <summary>
    /// Create a Message using JSON deserialization since Telegram.Bot.Types.Message has read-only properties.
    /// This is the same pattern used by Telegram.Bot internally.
    /// </summary>
    private static Message CreateMessage(int messageId, long chatId, TestMessageType messageType)
    {
        var json = messageType switch
        {
            TestMessageType.Photo => $$"""
            {
                "message_id": {{messageId}},
                "date": {{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}},
                "chat": {
                    "id": {{chatId}},
                    "type": "private"
                },
                "from": {
                    "id": 999,
                    "is_bot": false,
                    "first_name": "Admin"
                },
                "photo": [
                    {
                        "file_id": "photo123",
                        "file_unique_id": "unique123",
                        "width": 100,
                        "height": 100
                    }
                ],
                "caption": "Original caption"
            }
            """,
            TestMessageType.Video => $$"""
            {
                "message_id": {{messageId}},
                "date": {{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}},
                "chat": {
                    "id": {{chatId}},
                    "type": "private"
                },
                "from": {
                    "id": 999,
                    "is_bot": false,
                    "first_name": "Admin"
                },
                "video": {
                    "file_id": "video123",
                    "file_unique_id": "uniquevideo123",
                    "width": 1920,
                    "height": 1080,
                    "duration": 30
                },
                "caption": "Original video caption"
            }
            """,
            _ => $$"""
            {
                "message_id": {{messageId}},
                "date": {{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}},
                "chat": {
                    "id": {{chatId}},
                    "type": "private"
                },
                "from": {
                    "id": 999,
                    "is_bot": false,
                    "first_name": "Admin"
                },
                "text": "Original text"
            }
            """
        };

        return JsonSerializer.Deserialize<Message>(json, JsonSerializerOptions.Web)!;
    }

    private static ReportCallbackContext CreateTestContext()
    {
        return new ReportCallbackContext(
            Id: TestContextId,
            ReportId: TestReportId,
            ReportType: ReportType.ContentReport,
            ChatId: TestChatId,
            UserId: TestUserId,
            CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-5));
    }

    private static ReportBase CreateTestReport(
        ReportStatus status = ReportStatus.Pending,
        string? reviewedBy = null,
        string? actionTaken = null,
        DateTimeOffset? reviewedAt = null,
        int? reportCommandMessageId = null,
        ChatIdentity? chat = null)
    {
        return new ReportBase
        {
            Id = TestReportId,
            Type = ReportType.ContentReport,
            Chat = chat ?? new ChatIdentity(TestChatId, "Test Chat"),
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            Status = status,
            ReviewedBy = reviewedBy,
            ReviewedAt = reviewedAt,
            ActionTaken = actionTaken,
            AdminNotes = null,
            SubjectUserId = TestUserId,
            MessageId = TestMessageId,
            ReportCommandMessageId = reportCommandMessageId
        };
    }

    private static TelegramUser CreateTestUser()
    {
        return new TelegramUser(
            TelegramUserId: TestUserId,
            Username: "testuser",
            FirstName: "Test",
            LastName: "User",
            UserPhotoPath: null,
            PhotoHash: null,
            PhotoFileUniqueId: null,
            IsBot: false,
            IsTrusted: false,
            IsBanned: false,
            BotDmEnabled: false,
            FirstSeenAt: DateTimeOffset.UtcNow.AddDays(-30),
            LastSeenAt: DateTimeOffset.UtcNow.AddMinutes(-5),
            CreatedAt: DateTimeOffset.UtcNow.AddDays(-30),
            UpdatedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
            IsActive: true);
    }

    private void SetupValidContext()
    {
        var context = CreateTestContext();
        _mockCallbackContextRepo.GetByIdAsync(TestContextId, Arg.Any<CancellationToken>())
            .Returns(context);
    }

    private void SetupPendingReview()
    {
        var report = CreateTestReport(status: ReportStatus.Pending);
        _mockReportsRepo.GetByIdAsync(TestReportId, Arg.Any<CancellationToken>())
            .Returns(report);
    }

    private void SetupTargetUser()
    {
        var user = CreateTestUser();
        _mockUserRepo.GetByTelegramIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(user);
    }

    private static ReportCallbackContext CreateExamReviewContext()
    {
        return new ReportCallbackContext(
            Id: TestContextId,
            ReportId: TestReportId,
            ReportType: ReportType.ExamFailure, // Exam failure review context
            ChatId: TestChatId,
            UserId: TestUserId,
            CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-5));
    }

    private static ReportBase CreateExamReviewReport(
        ReportStatus status = ReportStatus.Pending,
        string? reviewedBy = null,
        string? actionTaken = null,
        DateTimeOffset? reviewedAt = null,
        ChatIdentity? chat = null)
    {
        return new ReportBase
        {
            Id = TestReportId,
            Type = ReportType.ExamFailure,
            Chat = chat ?? new ChatIdentity(TestChatId, "Test Chat"),
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            Status = status,
            ReviewedBy = reviewedBy,
            ReviewedAt = reviewedAt,
            ActionTaken = actionTaken,
            AdminNotes = null,
            SubjectUserId = TestUserId,
            MessageId = null // Exam failures don't have a message ID
        };
    }

    private void SetupExamReviewContext()
    {
        var context = CreateExamReviewContext();
        _mockCallbackContextRepo.GetByIdAsync(TestContextId, Arg.Any<CancellationToken>())
            .Returns(context);
    }

    private void SetupPendingExamReview()
    {
        var report = CreateExamReviewReport(status: ReportStatus.Pending);
        _mockReportsRepo.GetByIdAsync(TestReportId, Arg.Any<CancellationToken>())
            .Returns(report);
    }

    #endregion
}
