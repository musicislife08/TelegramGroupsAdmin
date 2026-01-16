using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.ContentDetection.Repositories;

/// <summary>
/// Unified repository for all review types (Report, ImpersonationAlert, ExamFailure).
/// Replaces IReportsRepository and IImpersonationAlertsRepository.
/// </summary>
public interface IReviewsRepository
{
    // ============================================================
    // Generic review operations (work for all types)
    // ============================================================

    /// <summary>
    /// Get a review by ID (any type)
    /// </summary>
    Task<Review?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get pending reviews, optionally filtered by chat and/or type.
    /// Ordered by creation date (newest first).
    /// </summary>
    Task<List<Review>> GetPendingAsync(
        long? chatId = null,
        ReviewType? type = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get reviews with optional filters.
    /// </summary>
    Task<List<Review>> GetAsync(
        long? chatId = null,
        ReviewType? type = null,
        ReportStatus? status = null,
        int limit = 100,
        int offset = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get count of pending reviews, optionally filtered by chat and/or type.
    /// </summary>
    Task<int> GetPendingCountAsync(
        long? chatId = null,
        ReviewType? type = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update review status and reviewer info.
    /// </summary>
    Task UpdateStatusAsync(
        long reviewId,
        ReportStatus status,
        string reviewedBy,
        string actionTaken,
        string? adminNotes = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically updates review status only if still pending.
    /// Returns false if review was already handled (race condition).
    /// </summary>
    Task<bool> TryUpdateStatusAsync(
        long reviewId,
        ReportStatus newStatus,
        string reviewedBy,
        string actionTaken,
        string? notes = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete old resolved reviews (cleanup). Returns count deleted.
    /// </summary>
    Task<int> DeleteOldReviewsAsync(
        DateTimeOffset olderThanTimestamp,
        ReviewType? type = null,
        CancellationToken cancellationToken = default);

    // ============================================================
    // Report-specific operations (Type = Report)
    // ============================================================

    /// <summary>
    /// Insert a new report review.
    /// </summary>
    Task<long> InsertReportAsync(Report report, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a message already has a pending report (duplicate prevention).
    /// </summary>
    Task<Report?> GetExistingPendingReportAsync(
        int messageId,
        long chatId,
        CancellationToken cancellationToken = default);

    // ============================================================
    // ImpersonationAlert-specific operations (Type = ImpersonationAlert)
    // ============================================================

    /// <summary>
    /// Insert a new impersonation alert review.
    /// </summary>
    Task<long> InsertImpersonationAlertAsync(
        ImpersonationAlertRecord alert,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get impersonation alert by ID with full context.
    /// </summary>
    Task<ImpersonationAlertRecord?> GetImpersonationAlertAsync(
        long id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get pending impersonation alerts, ordered by risk level then date.
    /// </summary>
    Task<List<ImpersonationAlertRecord>> GetPendingImpersonationAlertsAsync(
        long? chatId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a user has any pending impersonation alerts.
    /// </summary>
    Task<bool> HasPendingImpersonationAlertAsync(
        long suspectedUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all impersonation alerts for a specific user (suspected user ID).
    /// </summary>
    Task<List<ImpersonationAlertRecord>> GetImpersonationAlertHistoryAsync(
        long userId,
        CancellationToken cancellationToken = default);

    // ============================================================
    // ExamFailure-specific operations (Type = ExamFailure)
    // ============================================================

    /// <summary>
    /// Insert a new exam failure review.
    /// </summary>
    Task<long> InsertExamFailureAsync(
        ExamFailureRecord examFailure,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get exam failure by ID with full context.
    /// </summary>
    Task<ExamFailureRecord?> GetExamFailureAsync(
        long id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get pending exam failures for a specific chat.
    /// </summary>
    Task<List<ExamFailureRecord>> GetPendingExamFailuresAsync(
        long? chatId = null,
        CancellationToken cancellationToken = default);
}
