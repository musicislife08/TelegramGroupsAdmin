using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Telegram.Constants;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.BotCommands;
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
/// Unit tests for ReportCallbackHandler.
/// Tests callback parsing, context lookup, action execution, and cleanup.
/// Uses IModerationOrchestrator interface (enabled by Issue #127 interface extraction).
/// </summary>
[TestFixture]
public class ReportCallbackHandlerTests
{
    private const long TestContextId = 12345L;
    private const long TestReportId = 99L;
    private const long TestChatId = -100123456789L;
    private const long TestUserId = 54321L;
    private const int TestMessageId = 42;

    private ILogger<ReportCallbackHandler> _mockLogger = null!;
    private IServiceScopeFactory _mockScopeFactory = null!;
    private IServiceScope _mockScope = null!;
    private IServiceProvider _mockServiceProvider = null!;
    private ITelegramBotClientFactory _mockBotClientFactory = null!;
    private ITelegramOperations _mockOperations = null!;

    // Scoped services resolved from the mock scope
    private IReportCallbackContextRepository _mockCallbackContextRepo = null!;
    private IReportsRepository _mockReportsRepo = null!;
    private ITelegramUserRepository _mockUserRepo = null!;
    private IModerationOrchestrator _mockModerationService = null!;
    private IExamFlowService _mockExamFlowService = null!;

    private ReportCallbackHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _mockLogger = Substitute.For<ILogger<ReportCallbackHandler>>();
        _mockScopeFactory = Substitute.For<IServiceScopeFactory>();
        _mockScope = Substitute.For<IServiceScope>();
        _mockServiceProvider = Substitute.For<IServiceProvider>();
        _mockBotClientFactory = Substitute.For<ITelegramBotClientFactory>();
        _mockOperations = Substitute.For<ITelegramOperations>();

        // Scoped services
        _mockCallbackContextRepo = Substitute.For<IReportCallbackContextRepository>();
        _mockReportsRepo = Substitute.For<IReportsRepository>();
        _mockUserRepo = Substitute.For<ITelegramUserRepository>();
        _mockModerationService = Substitute.For<IModerationOrchestrator>();
        _mockExamFlowService = Substitute.For<IExamFlowService>();

        // Wire up scope factory → scope → service provider
        _mockScopeFactory.CreateScope().Returns(_mockScope);
        _mockScope.ServiceProvider.Returns(_mockServiceProvider);

        // Wire up service resolution
        _mockServiceProvider.GetService(typeof(IReportCallbackContextRepository))
            .Returns(_mockCallbackContextRepo);
        _mockServiceProvider.GetService(typeof(IReportsRepository))
            .Returns(_mockReportsRepo);
        _mockServiceProvider.GetService(typeof(ITelegramUserRepository))
            .Returns(_mockUserRepo);
        _mockServiceProvider.GetService(typeof(IModerationOrchestrator))
            .Returns(_mockModerationService);
        _mockServiceProvider.GetService(typeof(IExamFlowService))
            .Returns(_mockExamFlowService);

        // Bot client factory returns operations
        _mockBotClientFactory.GetOperationsAsync().Returns(_mockOperations);

        _handler = new ReportCallbackHandler(
            _mockLogger,
            _mockScopeFactory,
            _mockBotClientFactory);
    }

    [TearDown]
    public void TearDown()
    {
        _mockScope?.Dispose();
        _mockBotClientFactory?.Dispose();
    }

    #region CanHandle Tests

    [Test]
    public void CanHandle_ReportActionPrefix_ReturnsTrue()
    {
        // Arrange - callback data with report action prefix
        var callbackData = "rpt:123:0";

        // Act
        var result = _handler.CanHandle(callbackData);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    [TestCase("ban_select:123:456")]
    [TestCase("ban_cancel:456")]
    [TestCase("welcome:123")]
    [TestCase("other:data")]
    [TestCase("")]
    public void CanHandle_NonReportPrefix_ReturnsFalse(string callbackData)
    {
        // Act
        var result = _handler.CanHandle(callbackData);

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
        await _handler.HandleCallbackAsync(callbackQuery);

        // Assert - no scope created since we return early
        _mockScopeFactory.DidNotReceive().CreateScope();
    }

    [Test]
    public async Task HandleCallbackAsync_EmptyData_ReturnsEarly()
    {
        // Arrange
        var callbackQuery = CreateCallbackQuery(data: "");

        // Act
        await _handler.HandleCallbackAsync(callbackQuery);

        // Assert
        _mockScopeFactory.DidNotReceive().CreateScope();
    }

    [Test]
    public async Task HandleCallbackAsync_InvalidFormat_OnePart_ReturnsEarly()
    {
        // Arrange - only 1 part after prefix (need 2: contextId:action)
        var callbackQuery = CreateCallbackQuery(data: "rpt:123");

        // Act
        await _handler.HandleCallbackAsync(callbackQuery);

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
        await _handler.HandleCallbackAsync(callbackQuery);

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
        await _handler.HandleCallbackAsync(callbackQuery);

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
        await _handler.HandleCallbackAsync(callbackQuery);

        // Assert - message updated to show invalid action
        await _mockOperations.Received(1).EditMessageTextAsync(
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
        await _handler.HandleCallbackAsync(callbackQuery);

        // Assert - message updated to show expired
        await _mockOperations.Received(1).EditMessageTextAsync(
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
        await _handler.HandleCallbackAsync(callbackQuery);

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
        await _handler.HandleCallbackAsync(callbackQuery);

        // Assert - message updated
        await _mockOperations.Received(1).EditMessageTextAsync(
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
        await _handler.HandleCallbackAsync(callbackQuery);

        // Assert - Moderation should NOT be called for already-reviewed reports
        await _mockModerationService.DidNotReceive()
            .BanUserAsync(Arg.Any<long>(), Arg.Any<long?>(), Arg.Any<Actor>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>());

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
        await _handler.HandleCallbackAsync(callbackQuery);

        // Assert - message shows already handled by other admin
        await _mockOperations.Received().EditMessageTextAsync(
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
        await _handler.HandleCallbackAsync(callbackQuery);

        // Assert - message shows who already handled it
        await _mockOperations.Received().EditMessageTextAsync(
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
    public async Task HandleCallbackAsync_SpamAction_BansUserAndUpdatesReport()
    {
        // Arrange
        var callbackQuery = CreateCallbackQuery(data: $"rpt:{TestContextId}:0"); // Spam = 0
        SetupValidContext();
        SetupPendingReview();
        SetupTargetUser();

        _mockModerationService.BanUserAsync(
                TestUserId,
                TestMessageId,
                Arg.Any<Actor>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true, ChatsAffected = 5 });

        _mockReportsRepo.TryUpdateStatusAsync(
                Arg.Any<long>(), Arg.Any<ReportStatus>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        await _handler.HandleCallbackAsync(callbackQuery);

        // Assert - ban was called
        await _mockModerationService.Received(1).BanUserAsync(
            TestUserId,
            TestMessageId,
            Arg.Any<Actor>(),
            Arg.Is<string>(s => s.Contains("spam")),
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
    public async Task HandleCallbackAsync_SpamAction_BanFails_ReportsFailure()
    {
        // Arrange
        var callbackQuery = CreateCallbackQuery(data: $"rpt:{TestContextId}:0");
        SetupValidContext();
        SetupPendingReview();
        SetupTargetUser();

        _mockModerationService.BanUserAsync(
                TestUserId,
                TestMessageId,
                Arg.Any<Actor>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(ModerationResult.Failed("User is admin"));

        // Act
        await _handler.HandleCallbackAsync(callbackQuery);

        // Assert - message updated with failure
        await _mockOperations.Received().EditMessageTextAsync(
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
        var callbackQuery = CreateCallbackQuery(data: $"rpt:{TestContextId}:1"); // Warn = 1
        SetupValidContext();
        SetupPendingReview();
        SetupTargetUser();

        _mockModerationService.WarnUserAsync(
                TestUserId,
                TestMessageId,
                Arg.Any<Actor>(),
                Arg.Any<string>(),
                TestChatId,
                Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true, WarningCount = 2 });

        _mockReportsRepo.TryUpdateStatusAsync(
                Arg.Any<long>(), Arg.Any<ReportStatus>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        await _handler.HandleCallbackAsync(callbackQuery);

        // Assert
        await _mockModerationService.Received(1).WarnUserAsync(
            TestUserId,
            TestMessageId,
            Arg.Any<Actor>(),
            Arg.Is<string>(s => s.Contains("report")),
            TestChatId,
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
        var callbackQuery = CreateCallbackQuery(data: $"rpt:{TestContextId}:1");
        SetupValidContext();
        SetupPendingReview();
        SetupTargetUser();

        _mockModerationService.WarnUserAsync(
                TestUserId,
                TestMessageId,
                Arg.Any<Actor>(),
                Arg.Any<string>(),
                TestChatId,
                Arg.Any<CancellationToken>())
            .Returns(ModerationResult.Failed("Database error"));

        // Act
        await _handler.HandleCallbackAsync(callbackQuery);

        // Assert
        await _mockOperations.Received().EditMessageTextAsync(
            Arg.Any<long>(),
            Arg.Any<int>(),
            Arg.Is<string>(s => s.Contains("failed") || s.Contains("Database error")),
            replyMarkup: null,
            cancellationToken: Arg.Any<CancellationToken>());
    }

    #endregion

    #region TempBan Action Tests

    [Test]
    public async Task HandleCallbackAsync_TempBanAction_TempBansUserAndUpdatesReport()
    {
        // Arrange
        var callbackQuery = CreateCallbackQuery(data: $"rpt:{TestContextId}:2"); // TempBan = 2
        SetupValidContext();
        SetupPendingReview();
        SetupTargetUser();

        _mockModerationService.TempBanUserAsync(
                TestUserId,
                TestMessageId,
                Arg.Any<Actor>(),
                Arg.Any<string>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true, ChatsAffected = 3 });

        _mockReportsRepo.TryUpdateStatusAsync(
                Arg.Any<long>(), Arg.Any<ReportStatus>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        await _handler.HandleCallbackAsync(callbackQuery);

        // Assert
        await _mockModerationService.Received(1).TempBanUserAsync(
            TestUserId,
            TestMessageId,
            Arg.Any<Actor>(),
            Arg.Is<string>(s => s.Contains("report")),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>());

        await _mockReportsRepo.Received(1).TryUpdateStatusAsync(
            TestReportId,
            ReportStatus.Reviewed,
            Arg.Any<string>(),
            "TempBan",
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleCallbackAsync_TempBanAction_TempBanFails_ReportsFailure()
    {
        // Arrange
        var callbackQuery = CreateCallbackQuery(data: $"rpt:{TestContextId}:2");
        SetupValidContext();
        SetupPendingReview();
        SetupTargetUser();

        _mockModerationService.TempBanUserAsync(
                TestUserId,
                TestMessageId,
                Arg.Any<Actor>(),
                Arg.Any<string>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>())
            .Returns(ModerationResult.Failed("Rate limited"));

        // Act
        await _handler.HandleCallbackAsync(callbackQuery);

        // Assert
        await _mockOperations.Received().EditMessageTextAsync(
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
        await _handler.HandleCallbackAsync(callbackQuery);

        // Assert - no moderation action called
        await _mockModerationService.DidNotReceive()
            .BanUserAsync(Arg.Any<long>(), Arg.Any<long?>(), Arg.Any<Actor>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _mockModerationService.DidNotReceive()
            .WarnUserAsync(Arg.Any<long>(), Arg.Any<long?>(), Arg.Any<Actor>(),
                Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
        await _mockModerationService.DidNotReceive()
            .TempBanUserAsync(Arg.Any<long>(), Arg.Any<long?>(), Arg.Any<Actor>(),
                Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());

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
        await _handler.HandleCallbackAsync(callbackQuery);

        // Assert - sends reply to original message in chat
        await _mockOperations.Received().SendMessageAsync(
            chatId: TestChatId,
            text: Arg.Is<string>(s => s.Contains("reviewed") && s.Contains("no action")),
            replyParameters: Arg.Is<ReplyParameters>(r => r.MessageId == TestMessageId),
            cancellationToken: Arg.Any<CancellationToken>());
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
        await _handler.HandleCallbackAsync(callbackQuery);

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
        await _handler.HandleCallbackAsync(callbackQuery);

        // Assert - /report command message deleted
        await _mockOperations.Received().DeleteMessageAsync(
            TestChatId,
            reportCommandMessageId,
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

        _mockOperations.DeleteMessageAsync(TestChatId, reportCommandMessageId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Message not found"));

        // Act - should not throw
        await _handler.HandleCallbackAsync(callbackQuery);

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
        await _handler.HandleCallbackAsync(callbackQuery);

        // Assert - EditMessageTextAsync called (not EditMessageCaptionAsync)
        await _mockOperations.Received().EditMessageTextAsync(
            Arg.Any<long>(),
            Arg.Any<int>(),
            Arg.Any<string>(),
            replyMarkup: null,
            cancellationToken: Arg.Any<CancellationToken>());

        await _mockOperations.DidNotReceive().EditMessageCaptionAsync(
            Arg.Any<long>(), Arg.Any<int>(), Arg.Any<string>(),
            replyMarkup: Arg.Any<InlineKeyboardMarkup?>(),
            cancellationToken: Arg.Any<CancellationToken>());
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
        await _handler.HandleCallbackAsync(callbackQuery);

        // Assert - EditMessageCaptionAsync called (not EditMessageTextAsync)
        await _mockOperations.Received().EditMessageCaptionAsync(
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
        await _handler.HandleCallbackAsync(callbackQuery);

        // Assert - EditMessageCaptionAsync called for videos (like photos)
        await _mockOperations.Received().EditMessageCaptionAsync(
            Arg.Any<long>(),
            Arg.Any<int>(),
            Arg.Any<string>(),
            replyMarkup: null,
            cancellationToken: Arg.Any<CancellationToken>());

        // Verify text update was NOT called
        await _mockOperations.DidNotReceive().EditMessageTextAsync(
            Arg.Any<long>(), Arg.Any<int>(), Arg.Any<string>(),
            replyMarkup: Arg.Any<InlineKeyboardMarkup?>(),
            cancellationToken: Arg.Any<CancellationToken>());
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
        await _handler.HandleCallbackAsync(callbackQuery);

        // Assert - no message edit attempted since message is null
        await _mockOperations.DidNotReceive().EditMessageTextAsync(
            Arg.Any<long>(), Arg.Any<int>(), Arg.Any<string>(),
            replyMarkup: Arg.Any<InlineKeyboardMarkup?>(),
            cancellationToken: Arg.Any<CancellationToken>());

        await _mockOperations.DidNotReceive().EditMessageCaptionAsync(
            Arg.Any<long>(), Arg.Any<int>(), Arg.Any<string>(),
            replyMarkup: Arg.Any<InlineKeyboardMarkup?>(),
            cancellationToken: Arg.Any<CancellationToken>());

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

        _mockModerationService.BanUserAsync(
                TestUserId,
                TestMessageId,
                Arg.Any<Actor>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Unexpected error"));

        // Act
        await _handler.HandleCallbackAsync(callbackQuery);

        // Assert - message updated with failure
        await _mockOperations.Received().EditMessageTextAsync(
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
        int? reportCommandMessageId = null)
    {
        return new ReportBase
        {
            Id = TestReportId,
            Type = ReportType.ContentReport,
            ChatId = TestChatId,
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

    #endregion
}
