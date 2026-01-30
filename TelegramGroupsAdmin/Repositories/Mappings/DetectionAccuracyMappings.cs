using TelegramGroupsAdmin.Models.Analytics;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Repositories.Mappings;

/// <summary>
/// Mapping extensions for DetectionAccuracyView
/// Maps from view to a simpler model for C# consumption
/// </summary>
public static class DetectionAccuracyMappings
{
    extension(DataModels.DetectionAccuracyView view)
    {
        public DetectionAccuracyRecord ToModel()
        {
            return new DetectionAccuracyRecord
            {
                Id = view.Id,
                MessageId = view.MessageId,
                DetectedAt = view.DetectedAt,
                DetectionDate = view.DetectionDate,
                OriginalClassification = view.OriginalClassification,
                IsFalsePositive = view.IsFalsePositive,
                IsFalseNegative = view.IsFalseNegative
            };
        }
    }
}
