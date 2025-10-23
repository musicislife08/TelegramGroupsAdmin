using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// UI model for user tags (Phase 4.12: String-based tags with soft delete)
/// </summary>
public class UserTag
{
    public long Id { get; set; }
    public long TelegramUserId { get; set; }
    public string TagName { get; set; } = string.Empty;  // Lowercase tag name

    /// <summary>
    /// Display color for this tag (from TagDefinitions table)
    /// </summary>
    public TagColor TagColor { get; set; } = TagColor.Primary;

    /// <summary>
    /// Who added this tag (Phase 4.19: Actor system)
    /// </summary>
    public required Actor AddedBy { get; set; }

    public DateTimeOffset AddedAt { get; set; }
    public DateTimeOffset? RemovedAt { get; set; }

    /// <summary>
    /// Who removed this tag (Phase 4.19: Actor system, nullable until removed)
    /// </summary>
    public Actor? RemovedBy { get; set; }
}
