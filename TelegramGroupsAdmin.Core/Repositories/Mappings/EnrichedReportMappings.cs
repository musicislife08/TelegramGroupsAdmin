using System.Text.Json;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Core.Repositories.Mappings;

/// <summary>
/// Mapping extensions for EnrichedReportView → domain models.
/// The view provides pre-joined user/chat data, eliminating N+1 queries.
/// JSONB context is still parsed for type-specific fields.
/// </summary>
internal static class EnrichedReportMappings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Maps EnrichedReportView to ImpersonationAlertRecord.
    /// User data comes from view joins; scoring/verdict from JSONB context.
    /// </summary>
    public static ImpersonationAlertRecord? ToImpersonationAlert(this EnrichedReportView view)
    {
        if (view.Type != (short)ReportType.ImpersonationAlert)
            return null;

        if (string.IsNullOrEmpty(view.Context))
            return null;

        var alertContext = JsonSerializer.Deserialize<ImpersonationAlertContext>(view.Context, JsonOptions);
        if (alertContext == null)
            return null;

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
            Id = (int)view.Id,
            ChatId = view.ChatId,
            DetectedAt = view.ReportedAt,
            ReviewedByUserId = view.WebUserId,
            ReviewedAt = view.ReviewedAt,
            Verdict = verdict,

            // From JSONB context
            SuspectedUserId = alertContext.SuspectedUserId,
            TargetUserId = alertContext.TargetUserId,
            TotalScore = alertContext.TotalScore,
            RiskLevel = riskLevel,
            NameMatch = alertContext.NameMatch,
            PhotoMatch = alertContext.PhotoMatch,
            PhotoSimilarityScore = alertContext.PhotoSimilarity,
            AutoBanned = alertContext.AutoBanned,

            // From view joins (no more N+1!)
            SuspectedUserName = view.SuspectedUsername,
            SuspectedFirstName = view.SuspectedFirstName,
            SuspectedLastName = view.SuspectedLastName,
            SuspectedPhotoPath = view.SuspectedPhotoPath,
            TargetUserName = view.TargetUsername,
            TargetFirstName = view.TargetFirstName,
            TargetLastName = view.TargetLastName,
            TargetPhotoPath = view.TargetPhotoPath,
            ChatName = view.ChatName,
            ReviewedByEmail = view.ReviewerEmail ?? view.ReviewedBy
        };
    }

    /// <summary>
    /// Maps EnrichedReportView to ExamFailureRecord.
    /// User data comes from view joins; exam details from JSONB context.
    /// </summary>
    public static ExamFailureRecord? ToExamFailure(this EnrichedReportView view)
    {
        if (view.Type != (short)ReportType.ExamFailure)
            return null;

        if (string.IsNullOrEmpty(view.Context))
            return null;

        var examContext = JsonSerializer.Deserialize<ExamFailureContext>(view.Context, JsonOptions);
        if (examContext == null)
            return null;

        return new ExamFailureRecord
        {
            Id = view.Id,
            ChatId = view.ChatId,
            FailedAt = view.ReportedAt,
            ReviewedBy = view.ReviewedBy,
            ReviewedAt = view.ReviewedAt,
            ActionTaken = view.ActionTaken,
            AdminNotes = view.AdminNotes,

            // From JSONB context
            UserId = examContext.UserId,
            McAnswers = examContext.McAnswers,
            ShuffleState = examContext.ShuffleState,
            OpenEndedAnswer = examContext.OpenEndedAnswer,
            Score = examContext.Score,
            PassingThreshold = examContext.PassingThreshold,
            AiEvaluation = examContext.AiEvaluation,

            // From view joins (no more N+1!)
            UserName = view.ExamUsername,
            UserFirstName = view.ExamFirstName,
            UserLastName = view.ExamLastName,
            UserPhotoPath = view.ExamPhotoPath,
            ChatName = view.ChatName
        };
    }

    /// <summary>
    /// Maps EnrichedReportView to Report (ContentReport).
    /// ContentReports don't need JSONB parsing - all data is in columns.
    /// </summary>
    public static Report ToContentReport(this EnrichedReportView view)
    {
        // Report is a positional record - use constructor syntax
        return new Report(
            Id: view.Id,
            MessageId: view.MessageId,
            ChatId: view.ChatId,
            ReportCommandMessageId: view.ReportCommandMessageId,
            ReportedByUserId: view.ReportedByUserId,
            ReportedByUserName: view.ReportedByUserName,
            ReportedAt: view.ReportedAt,
            Status: (ReportStatus)view.Status,
            ReviewedBy: view.ReviewedBy,
            ReviewedAt: view.ReviewedAt,
            ActionTaken: view.ActionTaken,
            AdminNotes: view.AdminNotes,
            WebUserId: view.WebUserId
        );
    }

    /// <summary>
    /// Maps EnrichedReportView to ReportBase (generic, any type).
    /// </summary>
    public static ReportBase ToBaseModel(this EnrichedReportView view)
    {
        return new ReportBase
        {
            Id = view.Id,
            Type = (ReportType)view.Type,
            ChatId = view.ChatId,
            CreatedAt = view.ReportedAt,  // View column is 'reported_at', maps to domain 'CreatedAt'
            Status = (ReportStatus)view.Status,
            ReviewedBy = view.ReviewedBy,
            ReviewedAt = view.ReviewedAt,
            ActionTaken = view.ActionTaken,
            AdminNotes = view.AdminNotes,
            ChatName = view.ChatName
        };
    }
}
