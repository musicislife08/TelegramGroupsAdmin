using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories.Mappings;

/// <summary>
/// Mapping extensions for Threshold Recommendation records
/// </summary>
public static class ThresholdRecommendationMappings
{
    extension(DataModels.ThresholdRecommendationDto data)
    {
        public UiModels.ThresholdRecommendation ToModel(string? reviewedByUsername = null)
        {
            return new UiModels.ThresholdRecommendation
            {
                Id = data.Id,
                AlgorithmName = data.AlgorithmName,
                CurrentThreshold = data.CurrentThreshold,
                RecommendedThreshold = data.RecommendedThreshold,
                ConfidenceScore = data.ConfidenceScore,
                VetoRateBefore = data.VetoRateBefore,
                EstimatedVetoRateAfter = data.EstimatedVetoRateAfter,
                SampleVetoedMessageIds = data.SampleVetoedMessageIds?.ToList() ?? [],
                SpamFlagsCount = data.SpamFlagsCount,
                VetoedCount = data.VetoedCount,
                TrainingPeriodStart = data.TrainingPeriodStart,
                TrainingPeriodEnd = data.TrainingPeriodEnd,
                CreatedAt = data.CreatedAt,
                Status = data.Status,
                ReviewedByUserId = data.ReviewedByUserId,
                ReviewedByUsername = reviewedByUsername,
                ReviewedAt = data.ReviewedAt,
                ReviewNotes = data.ReviewNotes
            };
        }
    }

    extension(UiModels.ThresholdRecommendation ui)
    {
        public DataModels.ThresholdRecommendationDto ToDto()
        {
            return new DataModels.ThresholdRecommendationDto
            {
                Id = ui.Id,
                AlgorithmName = ui.AlgorithmName,
                CurrentThreshold = ui.CurrentThreshold,
                RecommendedThreshold = ui.RecommendedThreshold,
                ConfidenceScore = ui.ConfidenceScore,
                VetoRateBefore = ui.VetoRateBefore,
                EstimatedVetoRateAfter = ui.EstimatedVetoRateAfter,
                SampleVetoedMessageIds = ui.SampleVetoedMessageIds.ToArray(),
                SpamFlagsCount = ui.SpamFlagsCount,
                VetoedCount = ui.VetoedCount,
                TrainingPeriodStart = ui.TrainingPeriodStart,
                TrainingPeriodEnd = ui.TrainingPeriodEnd,
                CreatedAt = ui.CreatedAt,
                Status = ui.Status,
                ReviewedByUserId = ui.ReviewedByUserId,
                ReviewedAt = ui.ReviewedAt,
                ReviewNotes = ui.ReviewNotes
            };
        }
    }
}
