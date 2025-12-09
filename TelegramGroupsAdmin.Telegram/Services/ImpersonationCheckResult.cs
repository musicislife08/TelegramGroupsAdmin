using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Type of protected entity that may be impersonated
/// </summary>
public enum ProtectedEntityType
{
    /// <summary>Admin user (Telegram user who is admin in a managed chat)</summary>
    User,
    /// <summary>Managed chat group name</summary>
    Chat,
    /// <summary>Linked channel name/photo</summary>
    Channel
}

/// <summary>
/// Result of impersonation detection check
/// </summary>
public record ImpersonationCheckResult
{
    public bool ShouldTakeAction => TotalScore >= 50;
    public bool ShouldAutoBan => TotalScore >= 100;

    public int TotalScore { get; init; }
    public ImpersonationRiskLevel RiskLevel { get; init; }

    public long SuspectedUserId { get; init; }

    /// <summary>
    /// Target user ID (only set when TargetEntityType is User)
    /// </summary>
    public long TargetUserId { get; init; }

    /// <summary>
    /// Type of protected entity being impersonated (User, Chat, or Channel)
    /// </summary>
    public ProtectedEntityType TargetEntityType { get; init; } = ProtectedEntityType.User;

    /// <summary>
    /// Target entity ID (chat ID or channel ID when TargetEntityType is Chat/Channel, 0 when User)
    /// </summary>
    public long TargetEntityId { get; init; }

    /// <summary>
    /// Display name of the target entity (chat name or channel name)
    /// </summary>
    public string? TargetEntityName { get; init; }

    public long ChatId { get; init; }

    public bool NameMatch { get; init; }
    public bool PhotoMatch { get; init; }
    public double? PhotoSimilarityScore { get; init; }
}
