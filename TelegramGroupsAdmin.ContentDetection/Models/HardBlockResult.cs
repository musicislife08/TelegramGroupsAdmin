namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Result from hard block pre-filter check
/// </summary>
public record HardBlockResult(
    bool ShouldBlock,
    string? Reason,
    string? BlockedDomain
);
