using TelegramGroupsAdmin.ContentDetection.Constants;

namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Individual content check result stored in detection_results.check_results_json
/// Serialized/deserialized by CheckResultsSerializer in ContentDetection library
/// V2 format: additive scoring (0.0-5.0) with abstained flag
/// </summary>
public record CheckResult
{
    public CheckName CheckName { get; init; }
    public double Score { get; init; }
    public bool Abstained { get; init; }
    public string Details { get; init; } = string.Empty;
    public double ProcessingTimeMs { get; init; }
    public bool IsSpam => !Abstained && Score > 0;
}
