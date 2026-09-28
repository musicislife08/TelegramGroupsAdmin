using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories.Mappings;

namespace TelegramGroupsAdmin.ContentDetection.Repositories.Mappings;

/// <summary>
/// Mapping extensions for Detection Result records (Phase 4.19: Actor conversion)
/// </summary>
public static class DetectionResultMappings
{
    extension(DataModels.DetectionResultRecordDto data)
    {
        public UiModels.DetectionResultRecord ToModel(
            string? webUserEmail = null,
            string? telegramUsername = null,
            string? telegramFirstName = null,
            string? telegramLastName = null)
        {
            return new UiModels.DetectionResultRecord
            {
                Id = data.Id,
                MessageId = data.MessageId,
                ChatId = data.ChatId,
                DetectedAt = data.DetectedAt,
                DetectionMethod = data.DetectionMethod,
                Source = (VerdictSource)(data.Source ?? throw new InvalidOperationException($"detection_results {data.Id} has no source")),
                Classification = (VerdictClassification)(data.Classification ?? throw new InvalidOperationException($"detection_results {data.Id} has no classification")),
                Properties = data.Properties,
                AuditLogId = data.AuditLogId,
                Score = data.Score,
                Reason = data.Reason,
                AddedBy = ActorMappings.ToActor(data.WebUserId, data.TelegramUserId, data.SystemIdentifier, webUserEmail, telegramUsername, telegramFirstName, telegramLastName),
                CheckResultsJson = data.CheckResultsJson,  // Phase 2.6
                EditVersion = data.EditVersion,             // Phase 2.6
                UserId = 0, // Will be populated by repository join
                MessageText = null // Will be populated by repository join
            };
        }
    }
}
