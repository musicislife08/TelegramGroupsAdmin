using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories.Mappings;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.ContentDetection.Repositories;

/// <summary>
/// Unified repository for all review types (Report, ImpersonationAlert, ExamFailure).
/// Stores all reviews in the unified 'reviews' table with type-specific context in JSONB.
/// </summary>
public class ReviewsRepository : IReviewsRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<ReviewsRepository> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public ReviewsRepository(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<ReviewsRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    // ============================================================
    // Generic review operations (work for all types)
    // ============================================================

    public async Task<Review?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var result = await (
            from review in context.Reviews
            where review.Id == id
            join chat in context.ManagedChats on review.ChatId equals chat.ChatId into chatGroup
            from c in chatGroup.DefaultIfEmpty()
            select new
            {
                Review = review,
                ChatName = c != null ? c.ChatName : null
            }
        )
        .AsNoTracking()
        .FirstOrDefaultAsync(cancellationToken);

        return result?.Review.ToBaseModel(result.ChatName);
    }

    public async Task<List<Review>> GetPendingAsync(
        long? chatId = null,
        ReviewType? type = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.Reviews
            .AsNoTracking()
            .Where(r => r.Status == DataModels.ReportStatus.Pending);

        if (chatId.HasValue)
            query = query.Where(r => r.ChatId == chatId.Value);

        if (type.HasValue)
            query = query.Where(r => r.Type == type.Value);

        var results = await (
            from review in query
            join chat in context.ManagedChats on review.ChatId equals chat.ChatId into chatGroup
            from c in chatGroup.DefaultIfEmpty()
            orderby review.ReportedAt descending
            select new
            {
                Review = review,
                ChatName = c != null ? c.ChatName : null
            }
        ).ToListAsync(cancellationToken);

        return results.Select(r => r.Review.ToBaseModel(r.ChatName)).ToList();
    }

    public async Task<List<Review>> GetAsync(
        long? chatId = null,
        ReviewType? type = null,
        ReportStatus? status = null,
        int limit = 100,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.Reviews.AsNoTracking();

        if (chatId.HasValue)
            query = query.Where(r => r.ChatId == chatId.Value);

        if (type.HasValue)
            query = query.Where(r => r.Type == type.Value);

        if (status.HasValue)
            query = query.Where(r => r.Status == status.Value);

        var results = await (
            from review in query
            join chat in context.ManagedChats on review.ChatId equals chat.ChatId into chatGroup
            from c in chatGroup.DefaultIfEmpty()
            orderby review.ReportedAt descending
            select new
            {
                Review = review,
                ChatName = c != null ? c.ChatName : null
            }
        )
        .Skip(offset)
        .Take(limit)
        .ToListAsync(cancellationToken);

        return results.Select(r => r.Review.ToBaseModel(r.ChatName)).ToList();
    }

    public async Task<int> GetPendingCountAsync(
        long? chatId = null,
        ReviewType? type = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.Reviews
            .AsNoTracking()
            .Where(r => r.Status == DataModels.ReportStatus.Pending);

        if (chatId.HasValue)
            query = query.Where(r => r.ChatId == chatId.Value);

        if (type.HasValue)
            query = query.Where(r => r.Type == type.Value);

        return await query.CountAsync(cancellationToken);
    }

    public async Task UpdateStatusAsync(
        long reviewId,
        ReportStatus status,
        string reviewedBy,
        string actionTaken,
        string? adminNotes = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.Reviews.FindAsync([reviewId], cancellationToken);

        if (entity != null)
        {
            entity.Status = status;
            entity.ReviewedBy = reviewedBy;
            entity.ReviewedAt = DateTimeOffset.UtcNow;
            entity.ActionTaken = actionTaken;
            entity.AdminNotes = adminNotes;

            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Updated review {ReviewId} (type={Type}) to status {Status} by {ReviewedBy} (action: {ActionTaken})",
                reviewId,
                entity.Type,
                status,
                reviewedBy,
                actionTaken);
        }
    }

    public async Task<bool> TryUpdateStatusAsync(
        long reviewId,
        ReportStatus newStatus,
        string reviewedBy,
        string actionTaken,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var rowsAffected = await context.Reviews
            .Where(r => r.Id == reviewId && r.Status == DataModels.ReportStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, newStatus)
                .SetProperty(r => r.ReviewedBy, reviewedBy)
                .SetProperty(r => r.ActionTaken, actionTaken)
                .SetProperty(r => r.ReviewedAt, DateTimeOffset.UtcNow)
                .SetProperty(r => r.AdminNotes, notes),
                cancellationToken);

        if (rowsAffected > 0)
        {
            _logger.LogInformation(
                "Atomically updated review {ReviewId} to status {Status} by {ReviewedBy} (action: {ActionTaken})",
                reviewId,
                newStatus,
                reviewedBy,
                actionTaken);
        }

        return rowsAffected > 0;
    }

    public async Task<int> DeleteOldReviewsAsync(
        DateTimeOffset olderThanTimestamp,
        ReviewType? type = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.Reviews
            .Where(r => r.ReportedAt < olderThanTimestamp)
            .Where(r => r.Status != DataModels.ReportStatus.Pending);

        if (type.HasValue)
            query = query.Where(r => r.Type == type.Value);

        var deleted = await query.ExecuteDeleteAsync(cancellationToken);

        if (deleted > 0)
        {
            _logger.LogInformation(
                "Deleted {Count} old reviews (type={Type}, older than {Timestamp})",
                deleted,
                type?.ToString() ?? "all",
                olderThanTimestamp);
        }

        return deleted;
    }

    // ============================================================
    // Report-specific operations (Type = Report)
    // ============================================================

    public async Task<long> InsertReportAsync(Report report, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = report.ToDto();
        entity.Type = ReviewType.Report;

        context.Reviews.Add(entity);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Inserted report {ReportId} for message {MessageId} in chat {ChatId} by user {UserId}",
            entity.Id,
            report.MessageId,
            report.ChatId,
            report.ReportedByUserId);

        return entity.Id;
    }

    public async Task<Report?> GetExistingPendingReportAsync(
        int messageId,
        long chatId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.Reviews
            .AsNoTracking()
            .FirstOrDefaultAsync(r =>
                r.Type == ReviewType.Report &&
                r.MessageId == messageId &&
                r.ChatId == chatId &&
                r.Status == DataModels.ReportStatus.Pending,
                cancellationToken);

        return entity?.ToModel();
    }

    public async Task<Report?> GetReportAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var result = await (
            from review in context.Reviews
            where review.Id == id && review.Type == ReviewType.Report
            join chat in context.ManagedChats on review.ChatId equals chat.ChatId into chatGroup
            from c in chatGroup.DefaultIfEmpty()
            select new
            {
                Review = review,
                ChatName = c != null ? c.ChatName : null
            }
        )
        .AsNoTracking()
        .FirstOrDefaultAsync(cancellationToken);

        if (result == null)
            return null;

        return await HydrateReportAsync(context, result.Review, result.ChatName, cancellationToken);
    }

    public async Task<List<Report>> GetReportsAsync(
        long? chatId = null,
        ReportStatus? status = null,
        int limit = 100,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.Reviews
            .AsNoTracking()
            .Where(r => r.Type == ReviewType.Report);

        if (chatId.HasValue)
            query = query.Where(r => r.ChatId == chatId.Value);

        if (status.HasValue)
            query = query.Where(r => r.Status == status.Value);

        var results = await (
            from review in query
            join chat in context.ManagedChats on review.ChatId equals chat.ChatId into chatGroup
            from c in chatGroup.DefaultIfEmpty()
            orderby review.ReportedAt descending
            select new
            {
                Review = review,
                ChatName = c != null ? c.ChatName : null
            }
        )
        .Skip(offset)
        .Take(limit)
        .ToListAsync(cancellationToken);

        var reports = new List<Report>();
        foreach (var r in results)
        {
            var report = await HydrateReportAsync(context, r.Review, r.ChatName, cancellationToken);
            if (report != null)
                reports.Add(report);
        }

        return reports;
    }

    public async Task<List<Report>> GetPendingReportsAsync(
        long? chatId = null,
        CancellationToken cancellationToken = default)
    {
        return await GetReportsAsync(
            chatId: chatId,
            status: DataModels.ReportStatus.Pending,
            limit: 100,
            offset: 0,
            cancellationToken: cancellationToken);
    }

    // ============================================================
    // ImpersonationAlert-specific operations (Type = ImpersonationAlert)
    // ============================================================

    public async Task<long> InsertImpersonationAlertAsync(
        ImpersonationAlertRecord alert,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Build context JSONB
        var alertContext = new ImpersonationAlertContext
        {
            SuspectedUserId = alert.SuspectedUserId,
            TargetUserId = alert.TargetUserId,
            TotalScore = alert.TotalScore,
            RiskLevel = alert.RiskLevel.ToString().ToLowerInvariant(),
            NameMatch = alert.NameMatch,
            PhotoMatch = alert.PhotoMatch,
            PhotoSimilarity = alert.PhotoSimilarityScore,
            AutoBanned = alert.AutoBanned,
            Verdict = alert.Verdict?.ToString()
        };

        var entity = new ReviewDto
        {
            Type = ReviewType.ImpersonationAlert,
            ChatId = alert.ChatId,
            ReportedAt = alert.DetectedAt,
            Status = alert.ReviewedAt.HasValue ? DataModels.ReportStatus.Reviewed : DataModels.ReportStatus.Pending,
            ReviewedBy = alert.ReviewedByEmail,
            WebUserId = alert.ReviewedByUserId,
            ReviewedAt = alert.ReviewedAt,
            ActionTaken = alert.Verdict?.ToString(),
            Context = JsonSerializer.Serialize(alertContext, JsonOptions)
        };

        context.Reviews.Add(entity);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Created impersonation alert review #{ReviewId}: User {SuspectedUserId} → Admin {TargetUserId} (score: {Score}, auto_banned: {AutoBanned})",
            entity.Id,
            alert.SuspectedUserId,
            alert.TargetUserId,
            alert.TotalScore,
            alert.AutoBanned);

        return entity.Id;
    }

    public async Task<ImpersonationAlertRecord?> GetImpersonationAlertAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.Reviews
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id && r.Type == ReviewType.ImpersonationAlert, cancellationToken);

        if (entity == null)
            return null;

        return await HydrateImpersonationAlertAsync(context, entity, cancellationToken);
    }

    public async Task<List<ImpersonationAlertRecord>> GetPendingImpersonationAlertsAsync(
        long? chatId = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.Reviews
            .AsNoTracking()
            .Where(r => r.Type == ReviewType.ImpersonationAlert)
            .Where(r => r.Status == DataModels.ReportStatus.Pending);

        if (chatId.HasValue)
            query = query.Where(r => r.ChatId == chatId.Value);

        var entities = await query
            .OrderByDescending(r => r.ReportedAt)
            .ToListAsync(cancellationToken);

        var results = new List<ImpersonationAlertRecord>();
        foreach (var entity in entities)
        {
            var record = await HydrateImpersonationAlertAsync(context, entity, cancellationToken);
            if (record != null)
                results.Add(record);
        }

        // Sort by risk level (critical first), then by date
        return results
            .OrderByDescending(r => r.RiskLevel)
            .ThenByDescending(r => r.DetectedAt)
            .ToList();
    }

    public async Task<bool> HasPendingImpersonationAlertAsync(
        long suspectedUserId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // We need to check the Context JSONB for suspectedUserId
        // Using raw SQL for JSONB query efficiency
        var count = await context.Reviews
            .AsNoTracking()
            .Where(r => r.Type == ReviewType.ImpersonationAlert)
            .Where(r => r.Status == DataModels.ReportStatus.Pending)
            .Where(r => EF.Functions.JsonContains(r.Context!, $"{{\"suspectedUserId\":{suspectedUserId}}}"))
            .CountAsync(cancellationToken);

        return count > 0;
    }

    public async Task<List<ImpersonationAlertRecord>> GetImpersonationAlertHistoryAsync(
        long userId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Query by suspectedUserId in the JSONB context
        var entities = await context.Reviews
            .AsNoTracking()
            .Where(r => r.Type == ReviewType.ImpersonationAlert)
            .Where(r => EF.Functions.JsonContains(r.Context!, $"{{\"suspectedUserId\":{userId}}}"))
            .OrderByDescending(r => r.ReportedAt)
            .ToListAsync(cancellationToken);

        var results = new List<ImpersonationAlertRecord>();
        foreach (var entity in entities)
        {
            var record = await HydrateImpersonationAlertAsync(context, entity, cancellationToken);
            if (record != null)
                results.Add(record);
        }

        return results;
    }

    // ============================================================
    // ExamFailure-specific operations (Type = ExamFailure)
    // ============================================================

    public async Task<long> InsertExamFailureAsync(
        ExamFailureRecord examFailure,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Build context JSONB
        var examContext = new ExamFailureContext
        {
            UserId = examFailure.UserId,
            McAnswers = examFailure.McAnswers,
            OpenEndedAnswer = examFailure.OpenEndedAnswer,
            Score = examFailure.Score,
            PassingThreshold = examFailure.PassingThreshold,
            AiEvaluation = examFailure.AiEvaluation
        };

        var entity = new ReviewDto
        {
            Type = ReviewType.ExamFailure,
            ChatId = examFailure.ChatId,
            ReportedAt = examFailure.FailedAt,
            Status = DataModels.ReportStatus.Pending,
            Context = JsonSerializer.Serialize(examContext, JsonOptions)
        };

        context.Reviews.Add(entity);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Created exam failure review #{ReviewId}: User {UserId} in chat {ChatId} (score: {Score}/{Threshold})",
            entity.Id,
            examFailure.UserId,
            examFailure.ChatId,
            examFailure.Score,
            examFailure.PassingThreshold);

        return entity.Id;
    }

    public async Task<ExamFailureRecord?> GetExamFailureAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var result = await (
            from review in context.Reviews
            where review.Id == id && review.Type == ReviewType.ExamFailure
            join chat in context.ManagedChats on review.ChatId equals chat.ChatId into chatGroup
            from c in chatGroup.DefaultIfEmpty()
            select new
            {
                Review = review,
                ChatName = c != null ? c.ChatName : null
            }
        )
        .AsNoTracking()
        .FirstOrDefaultAsync(cancellationToken);

        if (result == null)
            return null;

        return await HydrateExamFailureAsync(context, result.Review, result.ChatName, cancellationToken);
    }

    public async Task<List<ExamFailureRecord>> GetPendingExamFailuresAsync(
        long? chatId = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.Reviews
            .AsNoTracking()
            .Where(r => r.Type == ReviewType.ExamFailure)
            .Where(r => r.Status == DataModels.ReportStatus.Pending);

        if (chatId.HasValue)
            query = query.Where(r => r.ChatId == chatId.Value);

        var results = await (
            from review in query
            join chat in context.ManagedChats on review.ChatId equals chat.ChatId into chatGroup
            from c in chatGroup.DefaultIfEmpty()
            orderby review.ReportedAt descending
            select new
            {
                Review = review,
                ChatName = c != null ? c.ChatName : null
            }
        ).ToListAsync(cancellationToken);

        var records = new List<ExamFailureRecord>();
        foreach (var r in results)
        {
            var record = await HydrateExamFailureAsync(context, r.Review, r.ChatName, cancellationToken);
            if (record != null)
                records.Add(record);
        }

        return records;
    }

    // ============================================================
    // Private helper methods
    // ============================================================

    private Task<Report?> HydrateReportAsync(
        AppDbContext context,
        ReviewDto entity,
        string? chatName,
        CancellationToken cancellationToken)
    {
        // Report model doesn't need additional hydration - all data is in ReviewDto columns
        // ChatName is available but Report model doesn't have that field currently
        _ = context;
        _ = chatName;
        _ = cancellationToken;
        return Task.FromResult<Report?>(entity.ToModel());
    }

    private async Task<ImpersonationAlertRecord?> HydrateImpersonationAlertAsync(
        AppDbContext context,
        ReviewDto entity,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(entity.Context))
            return null;

        var alertContext = JsonSerializer.Deserialize<ImpersonationAlertContext>(entity.Context, JsonOptions);
        if (alertContext == null)
            return null;

        // Fetch user details for display
        var suspectedUser = await context.TelegramUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.TelegramUserId == alertContext.SuspectedUserId, cancellationToken);

        var targetUser = await context.TelegramUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.TelegramUserId == alertContext.TargetUserId, cancellationToken);

        var chat = await context.ManagedChats
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ChatId == entity.ChatId, cancellationToken);

        string? reviewerEmail = null;
        if (!string.IsNullOrEmpty(entity.WebUserId))
        {
            var reviewer = await context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == entity.WebUserId, cancellationToken);
            reviewerEmail = reviewer?.Email;
        }

        // Parse risk level from string
        var riskLevel = Enum.TryParse<ImpersonationRiskLevel>(alertContext.RiskLevel, true, out var parsed)
            ? parsed
            : ImpersonationRiskLevel.Medium;

        // Parse verdict from string
        ImpersonationVerdict? verdict = null;
        if (!string.IsNullOrEmpty(alertContext.Verdict) &&
            Enum.TryParse<ImpersonationVerdict>(alertContext.Verdict, true, out var parsedVerdict))
        {
            verdict = parsedVerdict;
        }

        return new ImpersonationAlertRecord
        {
            Id = (int)entity.Id,
            SuspectedUserId = alertContext.SuspectedUserId,
            TargetUserId = alertContext.TargetUserId,
            ChatId = entity.ChatId,
            TotalScore = alertContext.TotalScore,
            RiskLevel = riskLevel,
            NameMatch = alertContext.NameMatch,
            PhotoMatch = alertContext.PhotoMatch,
            PhotoSimilarityScore = alertContext.PhotoSimilarity,
            DetectedAt = entity.ReportedAt,
            AutoBanned = alertContext.AutoBanned,
            ReviewedByUserId = entity.WebUserId,
            ReviewedAt = entity.ReviewedAt,
            Verdict = verdict,
            SuspectedUserName = suspectedUser?.Username,
            SuspectedFirstName = suspectedUser?.FirstName,
            SuspectedLastName = suspectedUser?.LastName,
            SuspectedPhotoPath = suspectedUser?.UserPhotoPath,
            TargetUserName = targetUser?.Username,
            TargetFirstName = targetUser?.FirstName,
            TargetLastName = targetUser?.LastName,
            TargetPhotoPath = targetUser?.UserPhotoPath,
            ChatName = chat?.ChatName,
            ReviewedByEmail = reviewerEmail ?? entity.ReviewedBy
        };
    }

    private async Task<ExamFailureRecord?> HydrateExamFailureAsync(
        AppDbContext context,
        ReviewDto entity,
        string? chatName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(entity.Context))
            return null;

        var examContext = JsonSerializer.Deserialize<ExamFailureContext>(entity.Context, JsonOptions);
        if (examContext == null)
            return null;

        // Fetch user details for display
        var user = await context.TelegramUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.TelegramUserId == examContext.UserId, cancellationToken);

        return new ExamFailureRecord
        {
            Id = entity.Id,
            ChatId = entity.ChatId,
            UserId = examContext.UserId,
            McAnswers = examContext.McAnswers,
            OpenEndedAnswer = examContext.OpenEndedAnswer,
            Score = examContext.Score,
            PassingThreshold = examContext.PassingThreshold,
            AiEvaluation = examContext.AiEvaluation,
            FailedAt = entity.ReportedAt,
            ReviewedBy = entity.ReviewedBy,
            ReviewedAt = entity.ReviewedAt,
            ActionTaken = entity.ActionTaken,
            AdminNotes = entity.AdminNotes,
            UserName = user?.Username,
            UserFirstName = user?.FirstName,
            UserLastName = user?.LastName,
            UserPhotoPath = user?.UserPhotoPath,
            ChatName = chatName
        };
    }
}
