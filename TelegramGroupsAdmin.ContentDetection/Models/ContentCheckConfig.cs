namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Content check configuration domain model (public API)
/// Represents per-chat configuration for individual content check algorithms
/// </summary>
public record ContentCheckConfig(
    long Id,
    long ChatId,
    string CheckName,
    bool Enabled,
    bool AlwaysRun,
    int? ConfidenceThreshold,
    string? ConfigurationJson,
    DateTimeOffset ModifiedDate,
    string? ModifiedBy
);
