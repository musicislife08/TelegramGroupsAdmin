using TelegramGroupsAdmin.Core.Repositories.Mappings;
using TelegramGroupsAdmin.Models.Analytics;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Repositories.Mappings;

/// <summary>
/// Mapping extensions for EnrichedDetectionView → RecentDetection
/// Uses ActorMappings.ToActor() for actor computation (keeps actor logic centralized)
/// </summary>
public static class EnrichedDetectionMappings
{
    extension(DataModels.EnrichedDetectionView view)
    {
        public RecentDetection ToModel()
        {
            return new RecentDetection
            {
                Id = view.Id,
                MessageId = view.MessageId,
                DetectedAt = view.DetectedAt,
                DetectionSource = view.DetectionSource,
                DetectionMethod = view.DetectionMethod,
                IsSpam = view.IsSpam,
                Confidence = view.Confidence,
                NetConfidence = view.NetConfidence,
                Reason = view.Reason,
                CheckResultsJson = view.CheckResultsJson,
                EditVersion = view.EditVersion,
                // Actor computation delegated to ActorMappings (single source of truth)
                AddedBy = ActorMappings.ToActor(
                    view.WebUserId,
                    view.TelegramUserId,
                    view.SystemIdentifier,
                    view.ActorWebEmail,
                    view.ActorTelegramUsername,
                    view.ActorTelegramFirstName,
                    view.ActorTelegramLastName),
                UserId = view.MessageUserId,
                MessageText = view.MessageText,
                ContentHash = view.ContentHash
            };
        }
    }
}
