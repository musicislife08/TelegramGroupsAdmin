using TelegramGroupsAdmin.ContentDetection.Constants;

namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Individual content check result stored in detection_results.check_results_json
/// Serialized/deserialized by CheckResultsSerializer in ContentDetection library
/// </summary>
public record CheckResult
{
    public CheckName CheckName { get; init; }
    public CheckResultType Result { get; init; }
    public int Confidence { get; init; }
    public string Reason { get; init; } = string.Empty;
}

/// <summary>
/// Wrapper for check results JSON structure
/// Matches database JSONB format: {"Checks": [...]}
/// </summary>
public record CheckResults
{
    public List<CheckResult> Checks { get; init; } = [];
}
