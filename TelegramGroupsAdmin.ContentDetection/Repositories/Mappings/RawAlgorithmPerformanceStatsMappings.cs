using TelegramGroupsAdmin.ContentDetection.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.ContentDetection.Repositories.Mappings;

/// <summary>
/// Mapping extensions for RawAlgorithmPerformanceStats (Phase 5)
/// Maps from Data layer DTO to ContentDetection UI model
/// </summary>
public static class RawAlgorithmPerformanceStatsMappings
{
    extension(DataModels.RawAlgorithmPerformanceStatsDto dto)
    {
        public RawAlgorithmPerformanceStats ToModel()
        {
            return new RawAlgorithmPerformanceStats
            {
                CheckNameEnum = dto.CheckNameEnum,
                TotalExecutions = dto.TotalExecutions,
                AverageMs = dto.AverageMs,
                P95Ms = dto.P95Ms,
                MaxMs = dto.MaxMs,
                MinMs = dto.MinMs,
                TotalTimeContribution = dto.TotalTimeContribution
            };
        }
    }
}
