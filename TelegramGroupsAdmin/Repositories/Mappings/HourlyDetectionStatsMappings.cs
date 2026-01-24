using TelegramGroupsAdmin.Models.Analytics;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Repositories.Mappings;

/// <summary>
/// Mapping extensions for HourlyDetectionStatsView
/// Maps from view to a simpler model for C# consumption
/// </summary>
public static class HourlyDetectionStatsMappings
{
    extension(DataModels.HourlyDetectionStatsView view)
    {
        public HourlyDetectionStats ToModel()
        {
            return new HourlyDetectionStats
            {
                DetectionDate = view.DetectionDate,
                DetectionHour = view.DetectionHour,
                TotalCount = (int)view.TotalCount,
                SpamCount = (int)view.SpamCount,
                HamCount = (int)view.HamCount,
                ManualCount = (int)view.ManualCount,
                AvgConfidence = view.AvgConfidence
            };
        }
    }
}
