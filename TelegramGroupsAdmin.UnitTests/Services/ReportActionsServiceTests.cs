using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Services;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using Report = TelegramGroupsAdmin.Core.Models.Report;
using ModerationResult = TelegramGroupsAdmin.Telegram.Services.Moderation.ModerationResult;
using ReportStatus = TelegramGroupsAdmin.Core.Models.ReportStatus;

namespace TelegramGroupsAdmin.UnitTests.Services;

/// <summary>
/// Unit tests for ReportActionsService.
/// Tests the web UI report actions (spam, ban, warn, dismiss).
/// Uses IBotModerationService for moderation workflows.
/// </summary>
[TestFixture]
public class ReportActionsServiceTests
{
    private const long TestReportId = 123L;
    private const long TestMessageId = 456L;
    private const long TestChatId = -100123456789L;
    private const long TestUserId = 789L;
    private const string TestReviewerId = "reviewer-user-id";
    private const string TestReviewerEmail = "reviewer@test.com";
    private static readonly Actor TestExecutor = Actor.FromWebUser(TestReviewerId, TestReviewerEmail);

    private IReportsRepository _mockReportsRepo = null!;
    private IMessageHistoryRepository _mockMessageRepo = null!;
    private IBotModerationService _mockModerationService = null!;
    private IAuditService _mockAuditService = null!;
    private IBotMessageService _mockBotMessageService = null!;
    private IReportCallbackContextRepository _mockCallbackContextRepo = null!;
    private ILogger<ReportActionsService> _mockLogger = null!;

    private ReportActionsService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _mockReportsRepo = Substitute.For<IReportsRepository>();
        _mockMessageRepo = Substitute.For<IMessageHistoryRepository>();
        _mockModerationService = Substitute.For<IBotModerationService>();
        _mockAuditService = Substitute.For<IAuditService>();
        _mockBotMessageService = Substitute.For<IBotMessageService>();
        _mockCallbackContextRepo = Substitute.For<IReportCallbackContextRepository>();
        _mockLogger = Substitute.For<ILogger<ReportActionsService>>();

        _service = new ReportActionsService(
            _mockReportsRepo,
            _mockMessageRepo,
            _mockModerationService,
            _mockAuditService,
            _mockBotMessageService,
            _mockCallbackContextRepo,
            _mockLogger);
    }

    #region HandleSpamActionAsync Tests

    [Test]
    public async Task HandleSpamActionAsync_Success_ExecutesModerationAndUpdatesReport()
    {
        // Arrange
        var report = CreateTestReport();
        var message = CreateTestMessage();
        SetupReportAndMessage(report, message);

        _mockModerationService.MarkAsSpamAndBanAsync(
                Arg.Any<SpamBanIntent>(),
                Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true, ChatsAffected = 5 });

        // Act
        await _service.HandleSpamActionAsync(TestReportId, TestExecutor);

        // Assert
        await _mockModerationService.Received(1).MarkAsSpamAndBanAsync(
            Arg.Is<SpamBanIntent>(i =>
                i.User.Id == TestUserId &&
                i.MessageId == TestMessageId &&
                i.Chat.Id == TestChatId &&
                i.Reason.Contains($"Report #{TestReportId}")),
            Arg.Any<CancellationToken>());

        await _mockReportsRepo.Received(1).UpdateStatusAsync(
            TestReportId,
            ReportStatus.Reviewed,
            TestReviewerEmail,
            "spam",
            Arg.Is<string>(s => s.Contains("5 chats")),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleSpamActionAsync_ReportNotFound_ThrowsInvalidOperationException()
    {
        // Arrange
        _mockReportsRepo.GetContentReportAsync(TestReportId, Arg.Any<CancellationToken>())
            .Returns((Report?)null);

        // Act & Assert
        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _service.HandleSpamActionAsync(TestReportId, TestExecutor));
        Assert.That(ex!.Message, Does.Contain($"Report {TestReportId} not found"));
    }

    [Test]
    public async Task HandleSpamActionAsync_MessageNotFound_ThrowsInvalidOperationException()
    {
        // Arrange
        var report = CreateTestReport();
        _mockReportsRepo.GetContentReportAsync(TestReportId, Arg.Any<CancellationToken>())
            .Returns(report);
        _mockMessageRepo.GetMessageAsync(TestMessageId, Arg.Any<CancellationToken>())
            .Returns((MessageRecord?)null);

        // Act & Assert
        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _service.HandleSpamActionAsync(TestReportId, TestExecutor));
        Assert.That(ex!.Message, Does.Contain($"Message {TestMessageId} not found"));
    }

    [Test]
    public async Task HandleSpamActionAsync_ModerationFails_ThrowsInvalidOperationException()
    {
        // Arrange
        var report = CreateTestReport();
        var message = CreateTestMessage();
        SetupReportAndMessage(report, message);

        _mockModerationService.MarkAsSpamAndBanAsync(
                Arg.Any<SpamBanIntent>(),
                Arg.Any<CancellationToken>())
            .Returns(ModerationResult.Failed("User is admin"));

        // Act & Assert
        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _service.HandleSpamActionAsync(TestReportId, TestExecutor));
        Assert.That(ex!.Message, Does.Contain("User is admin"));
    }

    [Test]
    public async Task HandleSpamActionAsync_Success_CreatesAuditLog()
    {
        // Arrange
        var report = CreateTestReport();
        var message = CreateTestMessage();
        SetupReportAndMessage(report, message);
        SetupSuccessfulModeration();

        // Act
        await _service.HandleSpamActionAsync(TestReportId, TestExecutor);

        // Assert - human-readable format: "Marked as spam (report #123, affected 5 chats)"
        await _mockAuditService.Received(1).LogEventAsync(
            AuditEventType.ReportReviewed,
            Arg.Is<Actor>(a => a.WebUserId == TestReviewerId),
            Arg.Is<Actor>(a => a.TelegramUserId == TestUserId),
            Arg.Is<string>(s => s.Contains("Marked as spam") && s.Contains($"report #{TestReportId}")),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleSpamActionAsync_Success_CleansUpCallbackContext()
    {
        // Arrange
        var report = CreateTestReport();
        var message = CreateTestMessage();
        SetupReportAndMessage(report, message);
        SetupSuccessfulModeration();

        // Act
        await _service.HandleSpamActionAsync(TestReportId, TestExecutor);

        // Assert
        await _mockCallbackContextRepo.Received(1)
            .DeleteByReportIdAsync(TestReportId, Arg.Any<CancellationToken>());
    }

    #endregion

    #region HandleBanActionAsync Tests

    [Test]
    public async Task HandleBanActionAsync_Success_BansUserAndDeletesMessage()
    {
        // Arrange
        var report = CreateTestReport();
        var message = CreateTestMessage();
        SetupReportAndMessage(report, message);

        _mockModerationService.BanUserAsync(
                Arg.Any<BanIntent>(),
                Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true, ChatsAffected = 3 });

        // Act
        await _service.HandleBanActionAsync(TestReportId, TestExecutor);

        // Assert
        await _mockModerationService.Received(1).BanUserAsync(
            Arg.Is<BanIntent>(i =>
                i.User.Id == TestUserId &&
                i.MessageId == TestMessageId &&
                i.Reason.Contains($"Report #{TestReportId}")),
            Arg.Any<CancellationToken>());

        await _mockBotMessageService.Received(1).DeleteAndMarkMessageAsync(
            TestChatId,
            Arg.Is<int>(i => i == (int)TestMessageId),
            deletionSource: "ban_action",
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleBanActionAsync_ModerationFails_ThrowsInvalidOperationException()
    {
        // Arrange
        var report = CreateTestReport();
        var message = CreateTestMessage();
        SetupReportAndMessage(report, message);

        _mockModerationService.BanUserAsync(
                Arg.Any<BanIntent>(),
                Arg.Any<CancellationToken>())
            .Returns(ModerationResult.Failed("Rate limited"));

        // Act & Assert
        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _service.HandleBanActionAsync(TestReportId, TestExecutor));
        Assert.That(ex!.Message, Does.Contain("Rate limited"));
    }

    [Test]
    public async Task HandleBanActionAsync_DeleteMessageFails_ContinuesExecution()
    {
        // Arrange
        var report = CreateTestReport();
        var message = CreateTestMessage();
        SetupReportAndMessage(report, message);

        _mockModerationService.BanUserAsync(
                Arg.Any<BanIntent>(),
                Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true, ChatsAffected = 3 });

        _mockBotMessageService.DeleteAndMarkMessageAsync(
                TestChatId,
                Arg.Any<int>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Message already deleted"));

        // Act - should not throw
        await _service.HandleBanActionAsync(TestReportId, TestExecutor);

        // Assert - report status still updated
        await _mockReportsRepo.Received(1).UpdateStatusAsync(
            TestReportId,
            ReportStatus.Reviewed,
            TestReviewerEmail,
            "ban",
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region HandleWarnActionAsync Tests

    [Test]
    public async Task HandleWarnActionAsync_Success_WarnsUserAndUpdatesReport()
    {
        // Arrange
        var report = CreateTestReport();
        var message = CreateTestMessage();
        SetupReportAndMessage(report, message);

        _mockModerationService.WarnUserAsync(
                Arg.Any<WarnIntent>(),
                Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true, WarningCount = 2 });

        // Act
        await _service.HandleWarnActionAsync(TestReportId, TestExecutor);

        // Assert
        await _mockModerationService.Received(1).WarnUserAsync(
            Arg.Is<WarnIntent>(i =>
                i.User.Id == TestUserId &&
                i.MessageId == TestMessageId &&
                i.Chat.Id == TestChatId &&
                i.Reason.Contains($"Report #{TestReportId}")),
            Arg.Any<CancellationToken>());

        await _mockReportsRepo.Received(1).UpdateStatusAsync(
            TestReportId,
            ReportStatus.Reviewed,
            TestReviewerEmail,
            "warn",
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleWarnActionAsync_ModerationFails_ThrowsInvalidOperationException()
    {
        // Arrange
        var report = CreateTestReport();
        var message = CreateTestMessage();
        SetupReportAndMessage(report, message);

        _mockModerationService.WarnUserAsync(
                Arg.Any<WarnIntent>(),
                Arg.Any<CancellationToken>())
            .Returns(ModerationResult.Failed("User not found"));

        // Act & Assert
        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _service.HandleWarnActionAsync(TestReportId, TestExecutor));
        Assert.That(ex!.Message, Does.Contain("User not found"));
    }

    [Test]
    public async Task HandleWarnActionAsync_Success_IncludesWarningCountInAuditLog()
    {
        // Arrange
        var report = CreateTestReport();
        var message = CreateTestMessage();
        SetupReportAndMessage(report, message);

        _mockModerationService.WarnUserAsync(
                Arg.Any<WarnIntent>(),
                Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true, WarningCount = 3 });

        // Act
        await _service.HandleWarnActionAsync(TestReportId, TestExecutor);

        // Assert - human-readable format: "Warned user (report #123, 3 warnings total)"
        await _mockAuditService.Received(1).LogEventAsync(
            AuditEventType.ReportReviewed,
            Arg.Any<Actor>(),
            Arg.Any<Actor>(),
            Arg.Is<string>(s => s.Contains("Warned user") && s.Contains("3 warnings total")),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region HandleDismissActionAsync Tests

    [Test]
    public async Task HandleDismissActionAsync_Success_DismissesReportWithoutModeration()
    {
        // Arrange
        var report = CreateTestReport();
        _mockReportsRepo.GetContentReportAsync(TestReportId, Arg.Any<CancellationToken>())
            .Returns(report);

        // Act
        await _service.HandleDismissActionAsync(TestReportId, TestExecutor, "Not spam");

        // Assert - no moderation action
        await _mockModerationService.DidNotReceive()
            .BanUserAsync(Arg.Any<BanIntent>(), Arg.Any<CancellationToken>());
        await _mockModerationService.DidNotReceive()
            .WarnUserAsync(Arg.Any<WarnIntent>(), Arg.Any<CancellationToken>());

        // Report updated to dismissed
        await _mockReportsRepo.Received(1).UpdateStatusAsync(
            TestReportId,
            ReportStatus.Dismissed,
            TestReviewerEmail,
            "dismiss",
            "Not spam",
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleDismissActionAsync_NullReason_UsesDefaultReason()
    {
        // Arrange
        var report = CreateTestReport();
        _mockReportsRepo.GetContentReportAsync(TestReportId, Arg.Any<CancellationToken>())
            .Returns(report);

        // Act
        await _service.HandleDismissActionAsync(TestReportId, TestExecutor, null);

        // Assert
        await _mockReportsRepo.Received(1).UpdateStatusAsync(
            TestReportId,
            ReportStatus.Dismissed,
            TestReviewerEmail,
            "dismiss",
            "No action needed",
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleDismissActionAsync_Success_SendsReplyToReportedMessage()
    {
        // Arrange
        var report = CreateTestReport();
        _mockReportsRepo.GetContentReportAsync(TestReportId, Arg.Any<CancellationToken>())
            .Returns(report);

        // Act
        await _service.HandleDismissActionAsync(TestReportId, TestExecutor);

        // Assert - reply sent to reported message (not command message)
        await _mockBotMessageService.Received(1).SendAndSaveMessageAsync(
            TestChatId,
            Arg.Is<string>(s => s.Contains("reviewed") && s.Contains("no action")),
            parseMode: ParseMode.Markdown,
            replyParameters: Arg.Is<ReplyParameters>(r => r.MessageId == (int)TestMessageId),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleDismissActionAsync_Success_DeletesReportCommandMessage()
    {
        // Arrange
        var reportCommandMessageId = 999;
        var report = CreateTestReport(reportCommandMessageId: reportCommandMessageId);
        _mockReportsRepo.GetContentReportAsync(TestReportId, Arg.Any<CancellationToken>())
            .Returns(report);

        // Act
        await _service.HandleDismissActionAsync(TestReportId, TestExecutor);

        // Assert - /report command deleted
        await _mockBotMessageService.Received(1).DeleteAndMarkMessageAsync(
            TestChatId,
            reportCommandMessageId,
            deletionSource: "report_reviewed",
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleDismissActionAsync_ReplyFails_ContinuesExecution()
    {
        // Arrange
        var report = CreateTestReport();
        _mockReportsRepo.GetContentReportAsync(TestReportId, Arg.Any<CancellationToken>())
            .Returns(report);

        _mockBotMessageService.SendAndSaveMessageAsync(
                TestChatId,
                Arg.Any<string>(),
                Arg.Any<ParseMode?>(),
                Arg.Any<ReplyParameters?>(),
                Arg.Any<InlineKeyboardMarkup?>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Message deleted"));

        // Act - should not throw
        await _service.HandleDismissActionAsync(TestReportId, TestExecutor);

        // Assert - report status still updated
        await _mockReportsRepo.Received(1).UpdateStatusAsync(
            TestReportId,
            ReportStatus.Dismissed,
            TestReviewerEmail,
            "dismiss",
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Helper Methods

    private static Report CreateTestReport(int? reportCommandMessageId = null)
    {
        return new Report(
            Id: TestReportId,
            MessageId: (int)TestMessageId,
            Chat: new ChatIdentity(TestChatId, "TestChat"),
            ReportCommandMessageId: reportCommandMessageId,
            ReportedByUserId: 11111L,
            ReportedByUserName: "reporter",
            ReportedAt: DateTimeOffset.UtcNow.AddMinutes(-10),
            Status: ReportStatus.Pending,
            ReviewedBy: null,
            ReviewedAt: null,
            ActionTaken: null,
            AdminNotes: null);
    }

    private static MessageRecord CreateTestMessage()
    {
        return new MessageRecord(
            MessageId: TestMessageId,
            User: new UserIdentity(TestUserId, "Test", "User", "testuser"),
            Chat: new ChatIdentity(TestChatId, "Test Chat"),
            Timestamp: DateTimeOffset.UtcNow.AddMinutes(-15),
            MessageText: "Spam message content",
            PhotoFileId: null,
            PhotoFileSize: null,
            Urls: null,
            EditDate: null,
            ContentHash: null,
            PhotoLocalPath: null,
            PhotoThumbnailPath: null,
            ChatIconPath: null,
            UserPhotoPath: null,
            DeletedAt: null,
            DeletionSource: null,
            ReplyToMessageId: null,
            ReplyToUser: null,
            ReplyToText: null,
            MediaType: null,
            MediaFileId: null,
            MediaFileSize: null,
            MediaFileName: null,
            MediaMimeType: null,
            MediaLocalPath: null,
            MediaDuration: null,
            Translation: null,
            ContentCheckSkipReason: ContentCheckSkipReason.NotSkipped);
    }

    private void SetupReportAndMessage(Report report, MessageRecord message)
    {
        _mockReportsRepo.GetContentReportAsync(TestReportId, Arg.Any<CancellationToken>())
            .Returns(report);
        _mockMessageRepo.GetMessageAsync(TestMessageId, Arg.Any<CancellationToken>())
            .Returns(message);
    }

    private void SetupSuccessfulModeration()
    {
        _mockModerationService.MarkAsSpamAndBanAsync(
                Arg.Any<SpamBanIntent>(),
                Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true, ChatsAffected = 5 });
    }

    #endregion
}
