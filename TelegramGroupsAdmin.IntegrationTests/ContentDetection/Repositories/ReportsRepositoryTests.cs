using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.ContentDetection.Repositories;

/// <summary>
/// Integration tests for ReportsRepository.
/// Uses Testcontainers PostgreSQL for real database testing of EF Core operations.
/// </summary>
/// <remarks>
/// These tests are critical for validating:
/// - TryUpdateStatusAsync atomic update (race condition handling)
/// - DeleteOldReportsAsync bulk delete with status filtering
/// - Basic CRUD operations
/// </remarks>
[TestFixture]
public class ReportsRepositoryTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private IServiceScope? _scope;
    private IReportsRepository? _repository;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseAndApplyMigrationsAsync();

        var services = new ServiceCollection();

        services.AddDbContextFactory<AppDbContext>(options =>
        {
            options.UseNpgsql(_testHelper.ConnectionString);
        });

        services.AddLogging(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning);
        });

        services.AddScoped<IReportsRepository, ReportsRepository>();

        _serviceProvider = services.BuildServiceProvider();

        _scope = _serviceProvider.CreateScope();
        _repository = _scope.ServiceProvider.GetRequiredService<IReportsRepository>();
    }

    [TearDown]
    public void TearDown()
    {
        _scope?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
        _testHelper?.Dispose();
    }

    #region Helper Methods

    private static Report CreateTestReport(
        long id = 0,
        int messageId = 12345,
        long chatId = -1001234567890,
        long? reportedByUserId = 987654321,
        ReportStatus status = ReportStatus.Pending,
        string? reviewedBy = null,
        DateTimeOffset? reviewedAt = null,
        string? actionTaken = null,
        DateTimeOffset? reportedAt = null)
    {
        return new Report(
            Id: id,
            MessageId: messageId,
            ChatId: chatId,
            ReportCommandMessageId: 99999,
            ReportedByUserId: reportedByUserId,
            ReportedByUserName: "TestUser",
            ReportedAt: reportedAt ?? DateTimeOffset.UtcNow,
            Status: status,
            ReviewedBy: reviewedBy,
            ReviewedAt: reviewedAt,
            ActionTaken: actionTaken,
            AdminNotes: null,
            WebUserId: null);
    }

    #endregion

    #region TryUpdateStatusAsync Tests (CRITICAL - Race Condition Handling)

    [Test]
    public async Task TryUpdateStatusAsync_WithPendingReport_UpdatesAndReturnsTrue()
    {
        // Arrange
        var report = CreateTestReport();
        var reportId = await _repository!.InsertContentReportAsync(report);

        // Act
        var result = await _repository.TryUpdateStatusAsync(
            reportId,
            ReportStatus.Reviewed,
            reviewedBy: "admin@test.com",
            actionTaken: "Spam",
            notes: "Marked as spam");

        // Assert
        Assert.That(result, Is.True);

        var updated = await _repository.GetByIdAsync(reportId);
        Assert.That(updated, Is.Not.Null);
        Assert.That(updated!.Status, Is.EqualTo(ReportStatus.Reviewed));
        Assert.That(updated.ReviewedBy, Is.EqualTo("admin@test.com"));
        Assert.That(updated.ActionTaken, Is.EqualTo("Spam"));
        Assert.That(updated.AdminNotes, Is.EqualTo("Marked as spam"));
    }

    [Test]
    public async Task TryUpdateStatusAsync_WithAlreadyReviewedReport_ReturnsFalse()
    {
        // Arrange - Insert and immediately mark as reviewed
        var report = CreateTestReport();
        var reportId = await _repository!.InsertContentReportAsync(report);

        // First update succeeds
        await _repository.TryUpdateStatusAsync(
            reportId,
            ReportStatus.Reviewed,
            reviewedBy: "first-admin@test.com",
            actionTaken: "Spam");

        // Act - Second update should fail (simulates race condition)
        var result = await _repository.TryUpdateStatusAsync(
            reportId,
            ReportStatus.Dismissed,
            reviewedBy: "second-admin@test.com",
            actionTaken: "Dismiss");

        // Assert
        Assert.That(result, Is.False, "Second update should fail because report is no longer Pending");

        // Verify original values preserved
        var finalReport = await _repository.GetByIdAsync(reportId);
        Assert.That(finalReport!.ReviewedBy, Is.EqualTo("first-admin@test.com"));
        Assert.That(finalReport.ActionTaken, Is.EqualTo("Spam"));
    }

    [Test]
    public async Task TryUpdateStatusAsync_WithNonExistentReport_ReturnsFalse()
    {
        // Arrange - Non-existent report ID
        const long nonExistentId = 999999999;

        // Act
        var result = await _repository!.TryUpdateStatusAsync(
            nonExistentId,
            ReportStatus.Reviewed,
            reviewedBy: "admin@test.com",
            actionTaken: "Spam");

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task TryUpdateStatusAsync_SetsAllFieldsCorrectly()
    {
        // Arrange
        var report = CreateTestReport();
        var reportId = await _repository!.InsertContentReportAsync(report);
        var beforeUpdate = DateTimeOffset.UtcNow;

        // Act
        await _repository.TryUpdateStatusAsync(
            reportId,
            ReportStatus.Dismissed,
            reviewedBy: "moderator@example.com",
            actionTaken: "FalsePositive",
            notes: "Not spam, legitimate message");

        // Assert
        var updated = await _repository.GetByIdAsync(reportId);
        Assert.That(updated, Is.Not.Null);
        Assert.That(updated!.Status, Is.EqualTo(ReportStatus.Dismissed));
        Assert.That(updated.ReviewedBy, Is.EqualTo("moderator@example.com"));
        Assert.That(updated.ActionTaken, Is.EqualTo("FalsePositive"));
        Assert.That(updated.AdminNotes, Is.EqualTo("Not spam, legitimate message"));
        Assert.That(updated.ReviewedAt, Is.Not.Null);
        Assert.That(updated.ReviewedAt!.Value, Is.GreaterThanOrEqualTo(beforeUpdate));
    }

    #endregion

    #region DeleteOldReportsAsync Tests

    [Test]
    public async Task DeleteOldReportsAsync_WithOldReports_DeletesAndReturnsCount()
    {
        // Arrange - Create old reviewed reports
        var oldDate = DateTimeOffset.UtcNow.AddDays(-60);

        var oldReport1 = CreateTestReport(messageId: 1, reportedAt: oldDate, status: ReportStatus.Reviewed);
        var oldReport2 = CreateTestReport(messageId: 2, reportedAt: oldDate, status: ReportStatus.Dismissed);
        var recentReport = CreateTestReport(messageId: 3, reportedAt: DateTimeOffset.UtcNow.AddDays(-1));

        await _repository!.InsertContentReportAsync(oldReport1);
        await _repository.InsertContentReportAsync(oldReport2);
        var recentId = await _repository.InsertContentReportAsync(recentReport);

        // Mark old reports as reviewed (so they can be deleted)
        await _repository.UpdateStatusAsync(
            await GetReportIdByMessageId(1), ReportStatus.Reviewed, "admin", "Spam");
        await _repository.UpdateStatusAsync(
            await GetReportIdByMessageId(2), ReportStatus.Dismissed, "admin", "Dismiss");

        // Act - Delete reports older than 30 days
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        var deleted = await _repository.DeleteOldReportsAsync(cutoff);

        // Assert
        Assert.That(deleted, Is.EqualTo(2));

        // Recent report should still exist
        var remaining = await _repository.GetByIdAsync(recentId);
        Assert.That(remaining, Is.Not.Null);
    }

    [Test]
    public async Task DeleteOldReportsAsync_SkipsPendingReports()
    {
        // Arrange - Create old PENDING report (should NOT be deleted)
        var oldDate = DateTimeOffset.UtcNow.AddDays(-60);
        var oldPendingReport = CreateTestReport(messageId: 100, reportedAt: oldDate, status: ReportStatus.Pending);
        var reportId = await _repository!.InsertContentReportAsync(oldPendingReport);

        // Act - Try to delete reports older than 30 days
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        var deleted = await _repository.DeleteOldReportsAsync(cutoff);

        // Assert - Pending report should NOT be deleted even if old
        Assert.That(deleted, Is.EqualTo(0));

        var stillExists = await _repository.GetByIdAsync(reportId);
        Assert.That(stillExists, Is.Not.Null, "Pending report should not be deleted regardless of age");
    }

    [Test]
    public async Task DeleteOldReportsAsync_WithNoMatchingReports_ReturnsZero()
    {
        // Arrange - Create only recent reports
        var recentReport = CreateTestReport(messageId: 200, reportedAt: DateTimeOffset.UtcNow);
        await _repository!.InsertContentReportAsync(recentReport);

        // Act
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        var deleted = await _repository.DeleteOldReportsAsync(cutoff);

        // Assert
        Assert.That(deleted, Is.EqualTo(0));
    }

    #endregion

    #region Basic CRUD Tests

    [Test]
    public async Task InsertAsync_WithValidReport_ReturnsId()
    {
        // Arrange
        var report = CreateTestReport();

        // Act
        var reportId = await _repository!.InsertContentReportAsync(report);

        // Assert
        Assert.That(reportId, Is.GreaterThan(0));
    }

    [Test]
    public async Task GetByIdAsync_WithExistingId_ReturnsReport()
    {
        // Arrange
        var report = CreateTestReport(
            messageId: 54321,
            chatId: -1009876543210,
            reportedByUserId: 111222333);
        var reportId = await _repository!.InsertContentReportAsync(report);

        // Act - Use GetContentReportAsync to get full Report type with ReportedByUserId
        var retrieved = await _repository.GetContentReportAsync(reportId);

        // Assert
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.Id, Is.EqualTo(reportId));
        Assert.That(retrieved.MessageId, Is.EqualTo(54321));
        Assert.That(retrieved.ChatId, Is.EqualTo(-1009876543210));
        Assert.That(retrieved.ReportedByUserId, Is.EqualTo(111222333));
        Assert.That(retrieved.Status, Is.EqualTo(ReportStatus.Pending));
    }

    [Test]
    public async Task GetByIdAsync_WithNonExistentId_ReturnsNull()
    {
        // Arrange
        const long nonExistentId = 999999999;

        // Act
        var result = await _repository!.GetByIdAsync(nonExistentId);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetPendingReportsAsync_ReturnsOnlyPendingReports()
    {
        // Arrange
        var pendingReport = CreateTestReport(messageId: 1);
        var reviewedReport = CreateTestReport(messageId: 2);

        var pendingId = await _repository!.InsertContentReportAsync(pendingReport);
        var reviewedId = await _repository.InsertContentReportAsync(reviewedReport);

        // Mark one as reviewed
        await _repository.UpdateStatusAsync(reviewedId, ReportStatus.Reviewed, "admin", "Spam");

        // Act
        var pending = await _repository.GetPendingContentReportsAsync();

        // Assert
        Assert.That(pending, Has.Count.EqualTo(1));
        Assert.That(pending[0].Id, Is.EqualTo(pendingId));
    }

    [Test]
    public async Task GetPendingCountAsync_ReturnsCorrectCount()
    {
        // Arrange
        await _repository!.InsertContentReportAsync(CreateTestReport(messageId: 1));
        await _repository.InsertContentReportAsync(CreateTestReport(messageId: 2));
        var reviewedId = await _repository.InsertContentReportAsync(CreateTestReport(messageId: 3));

        await _repository.UpdateStatusAsync(reviewedId, ReportStatus.Reviewed, "admin", "Spam");

        // Act
        var count = await _repository.GetPendingCountAsync();

        // Assert
        Assert.That(count, Is.EqualTo(2));
    }

    #endregion

    #region Test Helpers

    private async Task<long> GetReportIdByMessageId(int messageId)
    {
        var reports = await _repository!.GetContentReportsAsync();
        return reports.First(r => r.MessageId == messageId).Id;
    }

    #endregion

    #region ExamFailure Tests (Phase 2 - Entrance Exam)

    private static ExamFailureRecord CreateTestExamFailure(
        long chatId = -1001234567890,
        long userId = 123456789,
        int score = 40,
        int passingThreshold = 80,
        Dictionary<int, string>? mcAnswers = null,
        Dictionary<int, int[]>? shuffleState = null,
        string? openEndedAnswer = null,
        DateTimeOffset? failedAt = null)
    {
        return new ExamFailureRecord
        {
            ChatId = chatId,
            UserId = userId,
            McAnswers = mcAnswers ?? new Dictionary<int, string>
            {
                { 0, "A" },
                { 1, "C" },
                { 2, "B" }
            },
            ShuffleState = shuffleState ?? new Dictionary<int, int[]>
            {
                { 0, [0, 1, 2, 3] },
                { 1, [2, 0, 1, 3] },
                { 2, [1, 3, 0, 2] }
            },
            OpenEndedAnswer = openEndedAnswer,
            Score = score,
            PassingThreshold = passingThreshold,
            FailedAt = failedAt ?? DateTimeOffset.UtcNow
        };
    }

    [Test]
    public async Task InsertExamFailureAsync_WithValidData_ReturnsId()
    {
        // Arrange
        var examFailure = CreateTestExamFailure();

        // Act
        var id = await _repository!.InsertExamFailureAsync(examFailure);

        // Assert
        Assert.That(id, Is.GreaterThan(0));
    }

    [Test]
    public async Task GetExamFailureAsync_WithExistingId_ReturnsRecord()
    {
        // Arrange
        var examFailure = CreateTestExamFailure(
            chatId: -1009876543210,
            userId: 111222333,
            score: 60,
            passingThreshold: 80,
            openEndedAnswer: "I want to learn about crypto trading");

        var id = await _repository!.InsertExamFailureAsync(examFailure);

        // Act
        var retrieved = await _repository.GetExamFailureAsync(id);

        // Assert
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.Id, Is.EqualTo(id));
        Assert.That(retrieved.ChatId, Is.EqualTo(-1009876543210));
        Assert.That(retrieved.UserId, Is.EqualTo(111222333));
        Assert.That(retrieved.Score, Is.EqualTo(60));
        Assert.That(retrieved.PassingThreshold, Is.EqualTo(80));
        Assert.That(retrieved.OpenEndedAnswer, Is.EqualTo("I want to learn about crypto trading"));
    }

    [Test]
    public async Task GetExamFailureAsync_PreservesJsonbData()
    {
        // Arrange - Test JSONB serialization/deserialization
        var mcAnswers = new Dictionary<int, string>
        {
            { 0, "A" },
            { 1, "D" },
            { 2, "B" },
            { 3, "C" }
        };
        var shuffleState = new Dictionary<int, int[]>
        {
            { 0, [3, 2, 1, 0] },
            { 1, [0, 1, 2, 3] },
            { 2, [1, 0, 3, 2] },
            { 3, [2, 3, 0, 1] }
        };

        var examFailure = CreateTestExamFailure(
            mcAnswers: mcAnswers,
            shuffleState: shuffleState);

        var id = await _repository!.InsertExamFailureAsync(examFailure);

        // Act
        var retrieved = await _repository.GetExamFailureAsync(id);

        // Assert - JSONB should roundtrip correctly
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.McAnswers, Is.Not.Null);
        Assert.That(retrieved.McAnswers!.Count, Is.EqualTo(4));
        Assert.That(retrieved.McAnswers[0], Is.EqualTo("A"));
        Assert.That(retrieved.McAnswers[3], Is.EqualTo("C"));

        Assert.That(retrieved.ShuffleState, Is.Not.Null);
        Assert.That(retrieved.ShuffleState!.Count, Is.EqualTo(4));
        Assert.That(retrieved.ShuffleState[0], Is.EqualTo(new[] { 3, 2, 1, 0 }));
        Assert.That(retrieved.ShuffleState[2], Is.EqualTo(new[] { 1, 0, 3, 2 }));
    }

    [Test]
    public async Task GetExamFailuresAsync_WithPendingOnly_ReturnsOnlyPending()
    {
        // Arrange
        var pending1 = CreateTestExamFailure(userId: 1);
        var pending2 = CreateTestExamFailure(userId: 2);

        var id1 = await _repository!.InsertExamFailureAsync(pending1);
        await _repository.InsertExamFailureAsync(pending2);

        // Mark first as reviewed
        await _repository.TryUpdateStatusAsync(
            id1,
            ReportStatus.Reviewed,
            reviewedBy: "admin@test.com",
            actionTaken: "Approved");

        // Act
        var pending = await _repository.GetExamFailuresAsync(pendingOnly: true);

        // Assert - only the unreviewed one
        Assert.That(pending, Has.Count.EqualTo(1));
        Assert.That(pending[0].UserId, Is.EqualTo(2));
    }

    [Test]
    public async Task GetExamFailuresAsync_WithPendingOnlyFalse_ReturnsAll()
    {
        // Arrange
        var pending1 = CreateTestExamFailure(userId: 1);
        var pending2 = CreateTestExamFailure(userId: 2);

        var id1 = await _repository!.InsertExamFailureAsync(pending1);
        await _repository.InsertExamFailureAsync(pending2);

        // Mark first as reviewed
        await _repository.TryUpdateStatusAsync(
            id1,
            ReportStatus.Reviewed,
            reviewedBy: "admin@test.com",
            actionTaken: "Approved");

        // Act
        var all = await _repository.GetExamFailuresAsync(pendingOnly: false);

        // Assert - both items returned
        Assert.That(all, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GetExamFailuresAsync_FiltersByChatId()
    {
        // Arrange
        const long targetChatId = -1001111111111;

        await _repository!.InsertExamFailureAsync(CreateTestExamFailure(chatId: targetChatId, userId: 1));
        await _repository.InsertExamFailureAsync(CreateTestExamFailure(chatId: targetChatId, userId: 2));
        await _repository.InsertExamFailureAsync(CreateTestExamFailure(chatId: -1002222222222, userId: 3));

        // Act
        var pending = await _repository.GetExamFailuresAsync(chatId: targetChatId);

        // Assert
        Assert.That(pending, Has.Count.EqualTo(2));
        Assert.That(pending.All(p => p.ChatId == targetChatId), Is.True);
    }

    #endregion

    #region ImpersonationAlert Tests (Existing Feature - Enhanced Coverage)

    private static ImpersonationAlertRecord CreateTestImpersonationAlert(
        long suspectedUserId = 111111111,
        long targetUserId = 222222222,
        long chatId = -1001234567890,
        int totalScore = 85,
        ImpersonationRiskLevel riskLevel = ImpersonationRiskLevel.Critical,
        bool nameMatch = true,
        bool photoMatch = true,
        double? photoSimilarityScore = 0.92,
        DateTimeOffset? detectedAt = null)
    {
        return new ImpersonationAlertRecord
        {
            SuspectedUserId = suspectedUserId,
            TargetUserId = targetUserId,
            ChatId = chatId,
            TotalScore = totalScore,
            RiskLevel = riskLevel,
            NameMatch = nameMatch,
            PhotoMatch = photoMatch,
            PhotoSimilarityScore = photoSimilarityScore,
            DetectedAt = detectedAt ?? DateTimeOffset.UtcNow
        };
    }

    [Test]
    public async Task InsertImpersonationAlertAsync_WithValidData_ReturnsId()
    {
        // Arrange
        var alert = CreateTestImpersonationAlert();

        // Act
        var id = await _repository!.InsertImpersonationAlertAsync(alert);

        // Assert
        Assert.That(id, Is.GreaterThan(0));
    }

    [Test]
    public async Task GetImpersonationAlertAsync_WithExistingId_ReturnsRecord()
    {
        // Arrange
        var alert = CreateTestImpersonationAlert(
            suspectedUserId: 333333333,
            targetUserId: 444444444,
            totalScore: 75,
            riskLevel: ImpersonationRiskLevel.Medium);

        var id = await _repository!.InsertImpersonationAlertAsync(alert);

        // Act
        var retrieved = await _repository.GetImpersonationAlertAsync(id);

        // Assert
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.Id, Is.EqualTo((int)id));
        Assert.That(retrieved.SuspectedUserId, Is.EqualTo(333333333));
        Assert.That(retrieved.TargetUserId, Is.EqualTo(444444444));
        Assert.That(retrieved.TotalScore, Is.EqualTo(75));
        Assert.That(retrieved.RiskLevel, Is.EqualTo(ImpersonationRiskLevel.Medium));
    }

    [Test]
    public async Task HasPendingImpersonationAlertAsync_WithPendingAlert_ReturnsTrue()
    {
        // Arrange
        const long suspectedUserId = 555555555;
        var alert = CreateTestImpersonationAlert(suspectedUserId: suspectedUserId);
        await _repository!.InsertImpersonationAlertAsync(alert);

        // Act
        var hasPending = await _repository.HasPendingImpersonationAlertAsync(suspectedUserId);

        // Assert
        Assert.That(hasPending, Is.True);
    }

    [Test]
    public async Task HasPendingImpersonationAlertAsync_WithNoAlert_ReturnsFalse()
    {
        // Arrange
        const long suspectedUserId = 666666666;
        // No alert inserted

        // Act
        var hasPending = await _repository!.HasPendingImpersonationAlertAsync(suspectedUserId);

        // Assert
        Assert.That(hasPending, Is.False);
    }

    [Test]
    public async Task HasPendingImpersonationAlertAsync_WithReviewedAlert_ReturnsFalse()
    {
        // Arrange
        const long suspectedUserId = 777777777;
        var alert = CreateTestImpersonationAlert(suspectedUserId: suspectedUserId);
        var id = await _repository!.InsertImpersonationAlertAsync(alert);

        // Mark as reviewed
        await _repository.TryUpdateStatusAsync(
            id,
            ReportStatus.Reviewed,
            reviewedBy: "admin@test.com",
            actionTaken: "ConfirmScam");

        // Act
        var hasPending = await _repository.HasPendingImpersonationAlertAsync(suspectedUserId);

        // Assert
        Assert.That(hasPending, Is.False);
    }

    [Test]
    public async Task GetImpersonationAlertHistoryAsync_ReturnsAllAlertsForUser()
    {
        // Arrange
        const long suspectedUserId = 888888888;

        // Insert multiple alerts for same user (different target users)
        await _repository!.InsertImpersonationAlertAsync(
            CreateTestImpersonationAlert(suspectedUserId: suspectedUserId, targetUserId: 1));
        await _repository.InsertImpersonationAlertAsync(
            CreateTestImpersonationAlert(suspectedUserId: suspectedUserId, targetUserId: 2));
        await _repository.InsertImpersonationAlertAsync(
            CreateTestImpersonationAlert(suspectedUserId: 999999999, targetUserId: 3)); // Different user

        // Act
        var history = await _repository.GetImpersonationAlertHistoryAsync(suspectedUserId);

        // Assert
        Assert.That(history, Has.Count.EqualTo(2));
        Assert.That(history.All(h => h.SuspectedUserId == suspectedUserId), Is.True);
    }

    [Test]
    public async Task GetImpersonationAlertsAsync_OrdersByRiskLevelThenDate()
    {
        // Arrange - insert in random order (Medium=0, Critical=1)
        await _repository!.InsertImpersonationAlertAsync(
            CreateTestImpersonationAlert(suspectedUserId: 1, riskLevel: ImpersonationRiskLevel.Medium));
        await _repository.InsertImpersonationAlertAsync(
            CreateTestImpersonationAlert(suspectedUserId: 2, riskLevel: ImpersonationRiskLevel.Critical));
        await _repository.InsertImpersonationAlertAsync(
            CreateTestImpersonationAlert(suspectedUserId: 3, riskLevel: ImpersonationRiskLevel.Medium));

        // Act
        var pending = await _repository.GetImpersonationAlertsAsync(pendingOnly: true);

        // Assert - should be ordered by risk level (Critical first, then by detection date)
        Assert.That(pending, Has.Count.EqualTo(3));
        Assert.That(pending[0].RiskLevel, Is.EqualTo(ImpersonationRiskLevel.Critical));
    }

    #endregion
}
