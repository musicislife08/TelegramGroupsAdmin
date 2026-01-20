using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories.Mappings;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Core.Repositories;

/// <summary>
/// Unified repository for all report types (ContentReport, ImpersonationAlert, ExamFailure).
/// Uses enriched_reports view for efficient queries with pre-joined user/chat data.
/// </summary>
public class ReportsRepository : IReportsRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<ReportsRepository> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public ReportsRepository(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<ReportsRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    // ============================================================
    // Generic report operations (work for all types)
    // ============================================================

    public async Task<ReportBase?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var view = await context.EnrichedReports
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        return view?.ToBaseModel();
    }

    public async Task<List<ReportBase>> GetPendingAsync(
        long? chatId = null,
        ReportType? type = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.EnrichedReports
            .AsNoTracking()
            .Where(r => r.Status == (int)ReportStatus.Pending);

        if (chatId.HasValue)
            query = query.Where(r => r.ChatId == chatId.Value);

        if (type.HasValue)
            query = query.Where(r => r.Type == (short)type.Value);

        var results = await query
            .OrderByDescending(r => r.ReportedAt)
            .ToListAsync(cancellationToken);

        return results.Select(r => r.ToBaseModel()).ToList();
    }

    public async Task<List<ReportBase>> GetAsync(
        long? chatId = null,
        ReportType? type = null,
        ReportStatus? status = null,
        int limit = 100,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.EnrichedReports.AsNoTracking();

        if (chatId.HasValue)
            query = query.Where(r => r.ChatId == chatId.Value);

        if (type.HasValue)
            query = query.Where(r => r.Type == (short)type.Value);

        if (status.HasValue)
            query = query.Where(r => r.Status == (int)status.Value);

        var results = await query
            .OrderByDescending(r => r.ReportedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return results.Select(r => r.ToBaseModel()).ToList();
    }

    public async Task<int> GetPendingCountAsync(
        long? chatId = null,
        ReportType? type = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.Reports
            .AsNoTracking()
            .Where(r => r.Status == (int)ReportStatus.Pending);

        if (chatId.HasValue)
            query = query.Where(r => r.ChatId == chatId.Value);

        if (type.HasValue)
            query = query.Where(r => r.Type == (short)type.Value);

        return await query.CountAsync(cancellationToken);
    }

    public async Task UpdateStatusAsync(
        long reportId,
        ReportStatus status,
        string reviewedBy,
        string actionTaken,
        string? adminNotes = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.Reports.FindAsync([reportId], cancellationToken);

        if (entity != null)
        {
            entity.Status = (int)status;
            entity.ReviewedBy = reviewedBy;
            entity.ReviewedAt = DateTimeOffset.UtcNow;
            entity.ActionTaken = actionTaken;
            entity.AdminNotes = adminNotes;

            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Updated report {ReportId} (type={Type}) to status {Status} by {ReviewedBy} (action: {ActionTaken})",
                reportId,
                entity.Type,
                status,
                reviewedBy,
                actionTaken);
        }
    }

    public async Task<bool> TryUpdateStatusAsync(
        long reportId,
        ReportStatus newStatus,
        string reviewedBy,
        string actionTaken,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var rowsAffected = await context.Reports
            .Where(r => r.Id == reportId && r.Status == (int)ReportStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, (int)newStatus)
                .SetProperty(r => r.ReviewedBy, reviewedBy)
                .SetProperty(r => r.ActionTaken, actionTaken)
                .SetProperty(r => r.ReviewedAt, DateTimeOffset.UtcNow)
                .SetProperty(r => r.AdminNotes, notes),
                cancellationToken);

        if (rowsAffected > 0)
        {
            _logger.LogInformation(
                "Atomically updated report {ReportId} to status {Status} by {ReviewedBy} (action: {ActionTaken})",
                reportId,
                newStatus,
                reviewedBy,
                actionTaken);
        }

        return rowsAffected > 0;
    }

    public async Task<int> DeleteOldReportsAsync(
        DateTimeOffset olderThanTimestamp,
        ReportType? type = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.Reports
            .Where(r => r.ReportedAt < olderThanTimestamp)
            .Where(r => r.Status != (int)ReportStatus.Pending);

        if (type.HasValue)
            query = query.Where(r => r.Type == (short)type.Value);

        var deleted = await query.ExecuteDeleteAsync(cancellationToken);

        if (deleted > 0)
        {
            _logger.LogInformation(
                "Deleted {Count} old reports (type={Type}, older than {Timestamp})",
                deleted,
                type?.ToString() ?? "all",
                olderThanTimestamp);
        }

        return deleted;
    }

    // ============================================================
    // ContentReport-specific operations (Type = ContentReport)
    // ============================================================

    public async Task<long> InsertContentReportAsync(Report report, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = report.ToDto();
        entity.Type = (short)ReportType.ContentReport;

        context.Reports.Add(entity);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Inserted content report {ReportId} for message {MessageId} in chat {ChatId} by user {UserId}",
            entity.Id,
            report.MessageId,
            report.ChatId,
            report.ReportedByUserId);

        return entity.Id;
    }

    public async Task<Report?> GetExistingPendingContentReportAsync(
        int messageId,
        long chatId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.Reports
            .AsNoTracking()
            .FirstOrDefaultAsync(r =>
                r.Type == (short)ReportType.ContentReport &&
                r.MessageId == messageId &&
                r.ChatId == chatId &&
                r.Status == (int)ReportStatus.Pending,
                cancellationToken);

        return entity?.ToModel();
    }

    public async Task<Report?> GetContentReportAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var view = await context.EnrichedReports
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id && r.Type == (short)ReportType.ContentReport, cancellationToken);

        return view?.ToContentReport();
    }

    public async Task<List<Report>> GetContentReportsAsync(
        long? chatId = null,
        ReportStatus? status = null,
        int limit = 100,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.EnrichedReports
            .AsNoTracking()
            .Where(r => r.Type == (short)ReportType.ContentReport);

        if (chatId.HasValue)
            query = query.Where(r => r.ChatId == chatId.Value);

        if (status.HasValue)
            query = query.Where(r => r.Status == (int)status.Value);

        var results = await query
            .OrderByDescending(r => r.ReportedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return results.Select(r => r.ToContentReport()).ToList();
    }

    public async Task<List<Report>> GetPendingContentReportsAsync(
        long? chatId = null,
        CancellationToken cancellationToken = default)
    {
        return await GetContentReportsAsync(
            chatId: chatId,
            status: ReportStatus.Pending,
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

        var entity = new ReportDto
        {
            Type = (short)ReportType.ImpersonationAlert,
            ChatId = alert.ChatId,
            ReportedAt = alert.DetectedAt,
            Status = alert.ReviewedAt.HasValue ? (int)ReportStatus.Reviewed : (int)ReportStatus.Pending,
            ReviewedBy = alert.ReviewedByEmail,
            WebUserId = alert.ReviewedByUserId,
            ReviewedAt = alert.ReviewedAt,
            ActionTaken = alert.Verdict?.ToString(),
            Context = JsonSerializer.Serialize(alertContext, JsonOptions)
        };

        context.Reports.Add(entity);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Created impersonation alert #{ReportId}: User {SuspectedUserId} → Admin {TargetUserId} (score: {Score}, auto_banned: {AutoBanned})",
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

        var view = await context.EnrichedReports
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id && r.Type == (short)ReportType.ImpersonationAlert, cancellationToken);

        return view?.ToImpersonationAlert();
    }

    public async Task<List<ImpersonationAlertRecord>> GetImpersonationAlertsAsync(
        long? chatId = null,
        bool pendingOnly = true,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.EnrichedReports
            .AsNoTracking()
            .Where(r => r.Type == (short)ReportType.ImpersonationAlert);

        if (pendingOnly)
            query = query.Where(r => r.Status == (int)ReportStatus.Pending);

        if (chatId.HasValue)
            query = query.Where(r => r.ChatId == chatId.Value);

        var results = await query
            .OrderByDescending(r => r.ReportedAt)
            .ToListAsync(cancellationToken);

        // Map and sort by risk level (critical first), then by date
        return results
            .Select(r => r.ToImpersonationAlert())
            .Where(r => r != null)
            .Cast<ImpersonationAlertRecord>()
            .OrderByDescending(r => r.RiskLevel)
            .ThenByDescending(r => r.DetectedAt)
            .ToList();
    }

    public async Task<bool> HasPendingImpersonationAlertAsync(
        long suspectedUserId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Use JSONB query for efficiency
        var count = await context.Reports
            .AsNoTracking()
            .Where(r => r.Type == (short)ReportType.ImpersonationAlert)
            .Where(r => r.Status == (int)ReportStatus.Pending)
            .Where(r => EF.Functions.JsonContains(r.Context!, $"{{\"suspectedUserId\":{suspectedUserId}}}"))
            .CountAsync(cancellationToken);

        return count > 0;
    }

    public async Task<List<ImpersonationAlertRecord>> GetImpersonationAlertHistoryAsync(
        long userId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Use JSONB query to find by suspectedUserId, then join with view for enrichment
        var ids = await context.Reports
            .AsNoTracking()
            .Where(r => r.Type == (short)ReportType.ImpersonationAlert)
            .Where(r => EF.Functions.JsonContains(r.Context!, $"{{\"suspectedUserId\":{userId}}}"))
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        if (ids.Count == 0)
            return [];

        var results = await context.EnrichedReports
            .AsNoTracking()
            .Where(r => ids.Contains(r.Id))
            .OrderByDescending(r => r.ReportedAt)
            .ToListAsync(cancellationToken);

        return results
            .Select(r => r.ToImpersonationAlert())
            .Where(r => r != null)
            .Cast<ImpersonationAlertRecord>()
            .ToList();
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
            ShuffleState = examFailure.ShuffleState,
            OpenEndedAnswer = examFailure.OpenEndedAnswer,
            Score = examFailure.Score,
            PassingThreshold = examFailure.PassingThreshold,
            AiEvaluation = examFailure.AiEvaluation
        };

        var entity = new ReportDto
        {
            Type = (short)ReportType.ExamFailure,
            ChatId = examFailure.ChatId,
            ReportedAt = examFailure.FailedAt,
            Status = (int)ReportStatus.Pending,
            Context = JsonSerializer.Serialize(examContext, JsonOptions)
        };

        context.Reports.Add(entity);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Created exam failure report #{ReportId}: User {UserId} in chat {ChatId} (score: {Score}/{Threshold})",
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

        var view = await context.EnrichedReports
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id && r.Type == (short)ReportType.ExamFailure, cancellationToken);

        return view?.ToExamFailure();
    }

    public async Task<List<ExamFailureRecord>> GetExamFailuresAsync(
        long? chatId = null,
        bool pendingOnly = true,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.EnrichedReports
            .AsNoTracking()
            .Where(r => r.Type == (short)ReportType.ExamFailure);

        if (pendingOnly)
            query = query.Where(r => r.Status == (int)ReportStatus.Pending);

        if (chatId.HasValue)
            query = query.Where(r => r.ChatId == chatId.Value);

        var results = await query
            .OrderByDescending(r => r.ReportedAt)
            .ToListAsync(cancellationToken);

        return results
            .Select(r => r.ToExamFailure())
            .Where(r => r != null)
            .Cast<ExamFailureRecord>()
            .ToList();
    }
}
