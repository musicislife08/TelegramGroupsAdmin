namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Tag categories for user classification
/// </summary>
public enum TagType
{
    Suspicious = 0,
    VerifiedContributor = 1,
    SpamRisk = 2,
    SuspectedBot = 3,
    Impersonator = 4,
    Helpful = 5,
    Moderator = 6,
    Custom = 99
}

/// <summary>
/// UI model for user tags (Phase 4.19: Now uses Actor for attribution)
/// </summary>
public class UserTag
{
    public long Id { get; set; }
    public long TelegramUserId { get; set; }
    public TagType TagType { get; set; }
    public string? TagLabel { get; set; }

    /// <summary>
    /// Who added this tag (Phase 4.19: Actor system)
    /// </summary>
    public required Actor AddedBy { get; set; }

    public DateTimeOffset AddedAt { get; set; }
    public int? ConfidenceModifier { get; set; }
}
