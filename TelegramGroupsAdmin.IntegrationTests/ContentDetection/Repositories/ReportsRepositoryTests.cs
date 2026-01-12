using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
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
/// - TryUpdateReportStatusAsync atomic update (race condition handling)
/// - DeleteOldReportsAsync bulk delete with status filtering
/// - Basic CRUD operations
/// </remarks>
[TestFixture]
public class ReportsRepositoryTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
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

        var scope = _serviceProvider.CreateScope();
        _repository = scope.ServiceProvider.GetRequiredService<IReportsRepository>();
    }

    [TearDown]
    public void TearDown()
    {
        _testHelper?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
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

    #region TryUpdateReportStatusAsync Tests (CRITICAL - Race Condition Handling)

    [Test]
    public async Task TryUpdateReportStatusAsync_WithPendingReport_UpdatesAndReturnsTrue()
    {
        // Arrange
        var report = CreateTestReport();
        var reportId = await _repository!.InsertAsync(report);

        // Act
        var result = await _repository.TryUpdateReportStatusAsync(
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
    public async Task TryUpdateReportStatusAsync_WithAlreadyReviewedReport_ReturnsFalse()
    {
        // Arrange - Insert and immediately mark as reviewed
        var report = CreateTestReport();
        var reportId = await _repository!.InsertAsync(report);

        // First update succeeds
        await _repository.TryUpdateReportStatusAsync(
            reportId,
            ReportStatus.Reviewed,
            reviewedBy: "first-admin@test.com",
            actionTaken: "Spam");

        // Act - Second update should fail (simulates race condition)
        var result = await _repository.TryUpdateReportStatusAsync(
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
    public async Task TryUpdateReportStatusAsync_WithNonExistentReport_ReturnsFalse()
    {
        // Arrange - Non-existent report ID
        const long nonExistentId = 999999999;

        // Act
        var result = await _repository!.TryUpdateReportStatusAsync(
            nonExistentId,
            ReportStatus.Reviewed,
            reviewedBy: "admin@test.com",
            actionTaken: "Spam");

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task TryUpdateReportStatusAsync_SetsAllFieldsCorrectly()
    {
        // Arrange
        var report = CreateTestReport();
        var reportId = await _repository!.InsertAsync(report);
        var beforeUpdate = DateTimeOffset.UtcNow;

        // Act
        await _repository.TryUpdateReportStatusAsync(
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

        await _repository!.InsertAsync(oldReport1);
        await _repository.InsertAsync(oldReport2);
        var recentId = await _repository.InsertAsync(recentReport);

        // Mark old reports as reviewed (so they can be deleted)
        await _repository.UpdateReportStatusAsync(
            await GetReportIdByMessageId(1), ReportStatus.Reviewed, "admin", "Spam");
        await _repository.UpdateReportStatusAsync(
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
        var reportId = await _repository!.InsertAsync(oldPendingReport);

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
        await _repository!.InsertAsync(recentReport);

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
        var reportId = await _repository!.InsertAsync(report);

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
        var reportId = await _repository!.InsertAsync(report);

        // Act
        var retrieved = await _repository.GetByIdAsync(reportId);

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

        var pendingId = await _repository!.InsertAsync(pendingReport);
        var reviewedId = await _repository.InsertAsync(reviewedReport);

        // Mark one as reviewed
        await _repository.UpdateReportStatusAsync(reviewedId, ReportStatus.Reviewed, "admin", "Spam");

        // Act
        var pending = await _repository.GetPendingReportsAsync();

        // Assert
        Assert.That(pending, Has.Count.EqualTo(1));
        Assert.That(pending[0].Id, Is.EqualTo(pendingId));
    }

    [Test]
    public async Task GetPendingCountAsync_ReturnsCorrectCount()
    {
        // Arrange
        await _repository!.InsertAsync(CreateTestReport(messageId: 1));
        await _repository.InsertAsync(CreateTestReport(messageId: 2));
        var reviewedId = await _repository.InsertAsync(CreateTestReport(messageId: 3));

        await _repository.UpdateReportStatusAsync(reviewedId, ReportStatus.Reviewed, "admin", "Spam");

        // Act
        var count = await _repository.GetPendingCountAsync();

        // Assert
        Assert.That(count, Is.EqualTo(2));
    }

    #endregion

    #region Test Helpers

    private async Task<long> GetReportIdByMessageId(int messageId)
    {
        var reports = await _repository!.GetReportsAsync();
        return reports.First(r => r.MessageId == messageId).Id;
    }

    #endregion
}
