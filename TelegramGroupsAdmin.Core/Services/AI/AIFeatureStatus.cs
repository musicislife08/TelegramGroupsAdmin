namespace TelegramGroupsAdmin.Core.Services.AI;

/// <summary>
/// Status of an AI feature's configuration
/// </summary>
/// <param name="IsConfigured">Whether the feature has a connection and model assigned</param>
/// <param name="ConnectionEnabled">Whether the assigned connection is enabled</param>
/// <param name="RequiresVision">Whether the feature requires vision capability</param>
/// <param name="ConnectionId">ID of the assigned connection (if any)</param>
/// <param name="ModelName">Name of the assigned model (if any)</param>
public record AIFeatureStatus(
    bool IsConfigured,
    bool ConnectionEnabled,
    bool RequiresVision,
    string? ConnectionId,
    string? ModelName);
