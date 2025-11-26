namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Raw query result for algorithm performance stats with integer CheckName enum
/// Mapped to AlgorithmPerformanceStats with string CheckName in C# for compile-time safety
/// Configured as keyless entity type in AppDbContext for EF Core SqlQuery support
/// </summary>
public record RawAlgorithmPerformanceStats
{
    public int CheckNameEnum { get; init; }
    public int TotalExecutions { get; init; }
    public double AverageMs { get; init; }
    public double P95Ms { get; init; }
    public double MaxMs { get; init; }
    public double MinMs { get; init; }
    public double TotalTimeContribution { get; init; }
}
