using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.ContentDetection.Models;
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
            string? telegramFirstName = null)
        {
            return new UiModels.DetectionResultRecord
            {
                Id = data.Id,
                MessageId = data.MessageId,
                DetectedAt = data.DetectedAt,
                DetectionSource = data.DetectionSource,
                DetectionMethod = data.DetectionMethod,
                IsSpam = data.IsSpam,
                Confidence = data.Confidence,
                Reason = data.Reason,
                AddedBy = ActorMappings.ToActor(data.WebUserId, data.TelegramUserId, data.SystemIdentifier, webUserEmail, telegramUsername, telegramFirstName),
                UsedForTraining = data.UsedForTraining,
                NetConfidence = data.NetConfidence,
                CheckResultsJson = data.CheckResultsJson,  // Phase 2.6
                EditVersion = data.EditVersion,             // Phase 2.6
                UserId = 0, // Will be populated by repository join
                MessageText = null // Will be populated by repository join
            };
        }
    }

    extension(UiModels.DetectionResultRecord ui)
    {
        public DataModels.DetectionResultRecordDto ToDto()
        {
            ActorMappings.SetActorColumns(ui.AddedBy, out var webUserId, out var telegramUserId, out var systemIdentifier);

            return new()
            {
                Id = ui.Id,
                MessageId = ui.MessageId,
                DetectedAt = ui.DetectedAt,
                DetectionSource = ui.DetectionSource,
                DetectionMethod = ui.DetectionMethod,
                IsSpam = ui.IsSpam,
                Confidence = ui.Confidence,
                Reason = ui.Reason,
                WebUserId = webUserId,
                TelegramUserId = telegramUserId,
                SystemIdentifier = systemIdentifier,
                UsedForTraining = ui.UsedForTraining,
                NetConfidence = ui.NetConfidence,
                CheckResultsJson = ui.CheckResultsJson,  // Phase 2.6
                EditVersion = ui.EditVersion              // Phase 2.6
            };
        }
    }
}
