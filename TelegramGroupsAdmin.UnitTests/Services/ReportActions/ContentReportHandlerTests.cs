using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using TelegramGroupsAdmin.Telegram.Services.ReportActions;
using Report = TelegramGroupsAdmin.Core.Models.Report;
using ModerationResult = TelegramGroupsAdmin.Telegram.Services.Moderation.ModerationResult;
using ReportStatus = TelegramGroupsAdmin.Core.Models.ReportStatus;

namespace TelegramGroupsAdmin.UnitTests.Services.ReportActions;

[TestFixture]
public class ContentReportHandlerTests
{
    private const long TestReportId = 123L;
    private const int TestMessageId = 456;
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

    private ContentReportHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _mockReportsRepo = Substitute.For<IReportsRepository>();
        _mockMessageRepo = Substitute.For<IMessageHistoryRepository>();
        _mockModerationService = Substitute.For<IBotModerationService>();
        _mockAuditService = Substitute.For<IAuditService>();
        _mockBotMessageService = Substitute.For<IBotMessageService>();
        _mockCallbackContextRepo = Substitute.For<IReportCallbackContextRepository>();

        // Default: TryUpdateStatusAsync succeeds
        _mockReportsRepo.TryUpdateStatusAsync(
                Arg.Any<long>(), Arg.Any<ReportStatus>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        _handler = new ContentReportHandler(
            _mockReportsRepo,
            _mockMessageRepo,
            _mockModerationService,
            _mockAuditService,
            _mockBotMessageService,
            _mockCallbackContextRepo,
            NullLogger<ContentReportHandler>.Instance);
    }

    #region SpamAsync Tests

    [Test]
    public async Task SpamAsync_Success_ReturnsTrueWithChatsAffected()
    {
        var report = CreateTestReport();
        var message = CreateTestMessage();
        SetupReportAndMessage(report, message);

        _mockModerationService.MarkAsSpamAndBanAsync(
                Arg.Any<SpamBanIntent>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true, ChatsAffected = 5 });

        var result = await _handler.SpamAsync(TestReportId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Message, Does.Contain("5 chat(s)"));
        Assert.That(result.ActionName, Is.EqualTo("Spam"));
    }

    [Test]
    public async Task SpamAsync_Success_PassesCorrectIntentFields()
    {
        var report = CreateTestReport();
        var message = CreateTestMessage();
        SetupReportAndMessage(report, message);
        SetupSuccessfulSpamModeration();

        await _handler.SpamAsync(TestReportId, TestExecutor, CancellationToken.None);

        await _mockModerationService.Received(1).MarkAsSpamAndBanAsync(
            Arg.Is<SpamBanIntent>(i =>
                i.User.Id == TestUserId &&
                i.MessageId == TestMessageId &&
                i.Chat.Id == TestChatId &&
                i.Reason.Contains($"Report #{TestReportId}")),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SpamAsync_ReportNotFound_ReturnsFailure()
    {
        _mockReportsRepo.GetContentReportAsync(TestReportId, Arg.Any<CancellationToken>())
            .Returns((Report?)null);

        var result = await _handler.SpamAsync(TestReportId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain($"Report {TestReportId} not found"));
    }

    [Test]
    public async Task SpamAsync_MessageNotFound_ReturnsFailure()
    {
        var report = CreateTestReport();
        _mockReportsRepo.GetContentReportAsync(TestReportId, Arg.Any<CancellationToken>())
            .Returns(report);
        _mockMessageRepo.GetMessageAsync(TestMessageId, Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns((MessageRecord?)null);

        var result = await _handler.SpamAsync(TestReportId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain($"Message {TestMessageId} not found"));
    }

    [Test]
    public async Task SpamAsync_ModerationFails_ReturnsFailure()
    {
        var report = CreateTestReport();
        var message = CreateTestMessage();
        SetupReportAndMessage(report, message);

        _mockModerationService.MarkAsSpamAndBanAsync(
                Arg.Any<SpamBanIntent>(), Arg.Any<CancellationToken>())
            .Returns(ModerationResult.Failed("User is admin"));

        var result = await _handler.SpamAsync(TestReportId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("User is admin"));
    }

    [Test]
    public async Task SpamAsync_Success_CreatesAuditLog()
    {
        var report = CreateTestReport();
        var message = CreateTestMessage();
        SetupReportAndMessage(report, message);
        SetupSuccessfulSpamModeration();

        await _handler.SpamAsync(TestReportId, TestExecutor, CancellationToken.None);

        await _mockAuditService.Received(1).LogEventAsync(
            AuditEventType.ReportReviewed,
            Arg.Is<Actor>(a => a.WebUserId == TestReviewerId),
            Arg.Is<Actor>(a => a.TelegramUserId == TestUserId),
            Arg.Is<string>(s => s.Contains("Marked as spam") && s.Contains($"report #{TestReportId}")),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SpamAsync_Success_CleansUpCallbackContext()
    {
        var report = CreateTestReport();
        var message = CreateTestMessage();
        SetupReportAndMessage(report, message);
        SetupSuccessfulSpamModeration();

        await _handler.SpamAsync(TestReportId, TestExecutor, CancellationToken.None);

        await _mockCallbackContextRepo.Received(1)
            .DeleteByReportIdAsync(TestReportId, Arg.Any<CancellationToken>());
    }

    #endregion

    #region BanAsync Tests

    [Test]
    public async Task BanAsync_Success_BansUserAndDeletesMessage()
    {
        var report = CreateTestReport();
        var message = CreateTestMessage();
        SetupReportAndMessage(report, message);

        _mockModerationService.BanUserAsync(
                Arg.Any<BanIntent>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true, ChatsAffected = 3 });

        var result = await _handler.BanAsync(TestReportId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Message, Does.Contain("3 chat(s)"));
        Assert.That(result.ActionName, Is.EqualTo("Ban"));

        await _mockBotMessageService.Received(1).DeleteAndMarkMessageAsync(
            TestChatId, TestMessageId,
            deletionSource: "ban_action",
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task BanAsync_ModerationFails_ReturnsFailure()
    {
        var report = CreateTestReport();
        var message = CreateTestMessage();
        SetupReportAndMessage(report, message);

        _mockModerationService.BanUserAsync(
                Arg.Any<BanIntent>(), Arg.Any<CancellationToken>())
            .Returns(ModerationResult.Failed("Rate limited"));

        var result = await _handler.BanAsync(TestReportId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("Rate limited"));
    }

    [Test]
    public async Task BanAsync_DeleteMessageFails_ContinuesExecution()
    {
        var report = CreateTestReport();
        var message = CreateTestMessage();
        SetupReportAndMessage(report, message);

        _mockModerationService.BanUserAsync(
                Arg.Any<BanIntent>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true, ChatsAffected = 3 });

        _mockBotMessageService.DeleteAndMarkMessageAsync(
                TestChatId, Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Message already deleted"));

        var result = await _handler.BanAsync(TestReportId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        await _mockReportsRepo.Received(1).TryUpdateStatusAsync(
            TestReportId, ReportStatus.Reviewed, TestReviewerEmail,
            "ban", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region WarnAsync Tests

    [Test]
    public async Task WarnAsync_Success_WarnsUserAndUpdatesReport()
    {
        var report = CreateTestReport();
        var message = CreateTestMessage();
        SetupReportAndMessage(report, message);

        _mockModerationService.WarnUserAsync(
                Arg.Any<WarnIntent>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true, WarningCount = 2 });

        var result = await _handler.WarnAsync(TestReportId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Message, Does.Contain("warning #2"));
        Assert.That(result.ActionName, Is.EqualTo("Warn"));
    }

    [Test]
    public async Task WarnAsync_ModerationFails_ReturnsFailure()
    {
        var report = CreateTestReport();
        var message = CreateTestMessage();
        SetupReportAndMessage(report, message);

        _mockModerationService.WarnUserAsync(
                Arg.Any<WarnIntent>(), Arg.Any<CancellationToken>())
            .Returns(ModerationResult.Failed("User not found"));

        var result = await _handler.WarnAsync(TestReportId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("User not found"));
    }

    [Test]
    public async Task WarnAsync_Success_IncludesWarningCountInAuditLog()
    {
        var report = CreateTestReport();
        var message = CreateTestMessage();
        SetupReportAndMessage(report, message);

        _mockModerationService.WarnUserAsync(
                Arg.Any<WarnIntent>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true, WarningCount = 3 });

        await _handler.WarnAsync(TestReportId, TestExecutor, CancellationToken.None);

        await _mockAuditService.Received(1).LogEventAsync(
            AuditEventType.ReportReviewed, Arg.Any<Actor>(), Arg.Any<Actor>(),
            Arg.Is<string>(s => s.Contains("Warned user") && s.Contains("3 warnings total")),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region DismissAsync Tests

    [Test]
    public async Task DismissAsync_Success_DismissesReportWithoutModeration()
    {
        var report = CreateTestReport();
        _mockReportsRepo.GetContentReportAsync(TestReportId, Arg.Any<CancellationToken>())
            .Returns(report);

        var result = await _handler.DismissAsync(TestReportId, TestExecutor, "Not spam", CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.ActionName, Is.EqualTo("Dismiss"));

        await _mockModerationService.DidNotReceive()
            .BanUserAsync(Arg.Any<BanIntent>(), Arg.Any<CancellationToken>());

        await _mockReportsRepo.Received(1).TryUpdateStatusAsync(
            TestReportId, ReportStatus.Dismissed, TestReviewerEmail,
            "dismiss", "Not spam", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DismissAsync_NullReason_UsesDefaultReason()
    {
        var report = CreateTestReport();
        _mockReportsRepo.GetContentReportAsync(TestReportId, Arg.Any<CancellationToken>())
            .Returns(report);

        await _handler.DismissAsync(TestReportId, TestExecutor, null, CancellationToken.None);

        await _mockReportsRepo.Received(1).TryUpdateStatusAsync(
            TestReportId, ReportStatus.Dismissed, TestReviewerEmail,
            "dismiss", "No action needed", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DismissAsync_Success_SendsReplyToReportedMessage()
    {
        var report = CreateTestReport();
        _mockReportsRepo.GetContentReportAsync(TestReportId, Arg.Any<CancellationToken>())
            .Returns(report);

        await _handler.DismissAsync(TestReportId, TestExecutor, null, CancellationToken.None);

        await _mockBotMessageService.Received(1).SendAndSaveMessageAsync(
            TestChatId,
            Arg.Is<string>(s => s.Contains("reviewed") && s.Contains("no action")),
            parseMode: ParseMode.Markdown,
            replyParameters: Arg.Is<ReplyParameters>(r => r.MessageId == TestMessageId),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DismissAsync_Success_DeletesReportCommandMessage()
    {
        var reportCommandMessageId = 999;
        var report = CreateTestReport(reportCommandMessageId: reportCommandMessageId);
        _mockReportsRepo.GetContentReportAsync(TestReportId, Arg.Any<CancellationToken>())
            .Returns(report);

        await _handler.DismissAsync(TestReportId, TestExecutor, null, CancellationToken.None);

        await _mockBotMessageService.Received(1).DeleteAndMarkMessageAsync(
            TestChatId, reportCommandMessageId,
            deletionSource: "report_reviewed",
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DismissAsync_ReplyFails_ContinuesExecution()
    {
        var report = CreateTestReport();
        _mockReportsRepo.GetContentReportAsync(TestReportId, Arg.Any<CancellationToken>())
            .Returns(report);

        _mockBotMessageService.SendAndSaveMessageAsync(
                TestChatId, Arg.Any<string>(), Arg.Any<ParseMode?>(),
                Arg.Any<ReplyParameters?>(), Arg.Any<InlineKeyboardMarkup?>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Message deleted"));

        var result = await _handler.DismissAsync(TestReportId, TestExecutor, null, CancellationToken.None);

        Assert.That(result.Success, Is.True);
    }

    #endregion

    #region Status Guard Tests

    [Test]
    public async Task BanAsync_ReportAlreadyHandled_ReturnsFailureWithAttribution()
    {
        var report = new Report(
            Id: TestReportId, MessageId: TestMessageId,
            Chat: new ChatIdentity(TestChatId, "TestChat"),
            ReportCommandMessageId: null,
            ReportedByUserId: 11111L, ReportedByUserName: "reporter",
            ReportedAt: DateTimeOffset.UtcNow.AddMinutes(-10),
            Status: ReportStatus.Reviewed, ReviewedBy: "OtherAdmin",
            ReviewedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
            ActionTaken: "ban", AdminNotes: null);

        _mockReportsRepo.GetContentReportAsync(TestReportId, Arg.Any<CancellationToken>())
            .Returns(report);

        var result = await _handler.BanAsync(TestReportId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("Already handled by OtherAdmin"));
        Assert.That(result.Message, Does.Contain("(ban)"));
    }

    [Test]
    public async Task SpamAsync_ReportAlreadyDismissed_ReturnsFailureWithAttribution()
    {
        var report = new Report(
            Id: TestReportId, MessageId: TestMessageId,
            Chat: new ChatIdentity(TestChatId, "TestChat"),
            ReportCommandMessageId: null,
            ReportedByUserId: 11111L, ReportedByUserName: "reporter",
            ReportedAt: DateTimeOffset.UtcNow.AddMinutes(-10),
            Status: ReportStatus.Dismissed, ReviewedBy: "AdminX",
            ReviewedAt: DateTimeOffset.UtcNow.AddMinutes(-3),
            ActionTaken: "dismiss", AdminNotes: null);

        _mockReportsRepo.GetContentReportAsync(TestReportId, Arg.Any<CancellationToken>())
            .Returns(report);

        var result = await _handler.SpamAsync(TestReportId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("Already handled by AdminX"));
        Assert.That(result.Message, Does.Contain("(dismiss)"));

        await _mockModerationService.DidNotReceive().MarkAsSpamAndBanAsync(
            Arg.Any<SpamBanIntent>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task BanAsync_RaceCondition_TryUpdateFails_ReturnsFailureWithAttribution()
    {
        var report = CreateTestReport();
        var message = CreateTestMessage();
        SetupReportAndMessage(report, message);

        _mockModerationService.BanUserAsync(
                Arg.Any<BanIntent>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true, ChatsAffected = 3 });

        _mockReportsRepo.TryUpdateStatusAsync(
                Arg.Any<long>(), Arg.Any<ReportStatus>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var handledReport = new Report(
            Id: TestReportId, MessageId: TestMessageId,
            Chat: new ChatIdentity(TestChatId, "TestChat"),
            ReportCommandMessageId: null,
            ReportedByUserId: 11111L, ReportedByUserName: "reporter",
            ReportedAt: DateTimeOffset.UtcNow.AddMinutes(-10),
            Status: ReportStatus.Reviewed, ReviewedBy: "RacingAdmin",
            ReviewedAt: DateTimeOffset.UtcNow, ActionTaken: "spam",
            AdminNotes: null);

        // First call returns pending, second (re-fetch) returns handled
        _mockReportsRepo.GetContentReportAsync(TestReportId, Arg.Any<CancellationToken>())
            .Returns(report, handledReport);

        var result = await _handler.BanAsync(TestReportId, TestExecutor, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("Already handled by RacingAdmin"));
        Assert.That(result.Message, Does.Contain("(spam)"));
    }

    #endregion

    #region Helper Methods

    private static Report CreateTestReport(int? reportCommandMessageId = null)
    {
        return new Report(
            Id: TestReportId, MessageId: TestMessageId,
            Chat: new ChatIdentity(TestChatId, "TestChat"),
            ReportCommandMessageId: reportCommandMessageId,
            ReportedByUserId: 11111L, ReportedByUserName: "reporter",
            ReportedAt: DateTimeOffset.UtcNow.AddMinutes(-10),
            Status: ReportStatus.Pending,
            ReviewedBy: null, ReviewedAt: null,
            ActionTaken: null, AdminNotes: null);
    }

    private static MessageRecord CreateTestMessage()
    {
        return new MessageRecord(
            MessageId: TestMessageId,
            User: new UserIdentity(TestUserId, "Test", "User", "testuser"),
            Chat: new ChatIdentity(TestChatId, "Test Chat"),
            Timestamp: DateTimeOffset.UtcNow.AddMinutes(-15),
            MessageText: "Spam message content",
            PhotoFileId: null, PhotoFileSize: null, Urls: null,
            EditDate: null, ContentHash: null,
            PhotoLocalPath: null, PhotoThumbnailPath: null,
            ChatIconPath: null, UserPhotoPath: null,
            DeletedAt: null, DeletionSource: null,
            ReplyToMessageId: null, ReplyToUser: null, ReplyToText: null,
            MediaType: null, MediaFileId: null, MediaFileSize: null,
            MediaFileName: null, MediaMimeType: null, MediaLocalPath: null,
            MediaDuration: null, Translation: null,
            ContentCheckSkipReason: ContentCheckSkipReason.NotSkipped);
    }

    private void SetupReportAndMessage(Report report, MessageRecord message)
    {
        _mockReportsRepo.GetContentReportAsync(TestReportId, Arg.Any<CancellationToken>())
            .Returns(report);
        _mockMessageRepo.GetMessageAsync(TestMessageId, Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(message);
    }

    private void SetupSuccessfulSpamModeration()
    {
        _mockModerationService.MarkAsSpamAndBanAsync(
                Arg.Any<SpamBanIntent>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true, ChatsAffected = 5 });
    }

    #endregion
}
