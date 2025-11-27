namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// Database DTO for algorithm performance query results
/// Maps to ContentDetection.Models.RawAlgorithmPerformanceStats via repository mapping
/// Configured as keyless entity for SqlQuery support (Phase 5)
/// </summary>
public record RawAlgorithmPerformanceStatsDto
{
    public int CheckNameEnum { get; init; }
    public int TotalExecutions { get; init; }
    public double AverageMs { get; init; }
    public double P95Ms { get; init; }
    public double MaxMs { get; init; }
    public double MinMs { get; init; }
    public double TotalTimeContribution { get; init; }
}
