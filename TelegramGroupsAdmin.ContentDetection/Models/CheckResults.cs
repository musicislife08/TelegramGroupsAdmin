namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Wrapper for check results JSON structure.
/// Matches database JSONB format: {"Checks": [...]}
/// </summary>
public record CheckResults
{
    public List<CheckResult> Checks { get; init; } = [];
}
