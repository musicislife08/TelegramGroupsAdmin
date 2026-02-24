using TelegramGroupsAdmin.Core.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories.Mappings;

/// <summary>
/// Mapping extensions for profile scan result records.
/// </summary>
public static class ProfileScanResultMappings
{
    extension(DataModels.ProfileScanResultDto data)
    {
        public UiModels.ProfileScanResultRecord ToModel() => new(
            Id: data.Id,
            UserId: data.UserId,
            ScannedAt: data.ScannedAt,
            Score: data.Score,
            Outcome: (ProfileScanOutcome)data.Outcome,
            RuleScore: data.RuleScore,
            AiScore: data.AiScore,
            AiConfidence: data.AiConfidence,
            AiReason: data.AiReason,
            AiSignals: data.AiSignals);
    }

    extension(UiModels.ProfileScanResultRecord ui)
    {
        public DataModels.ProfileScanResultDto ToDto() => new()
        {
            Id = ui.Id,
            UserId = ui.UserId,
            ScannedAt = ui.ScannedAt,
            Score = ui.Score,
            Outcome = (int)ui.Outcome,
            RuleScore = ui.RuleScore,
            AiScore = ui.AiScore,
            AiConfidence = ui.AiConfidence,
            AiReason = ui.AiReason,
            AiSignals = ui.AiSignals
        };
    }
}
