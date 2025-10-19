using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// UI model for admin notes on Telegram users (Phase 4.19: Now uses Actor for attribution)
/// </summary>
public class AdminNote
{
    public long Id { get; set; }
    public long TelegramUserId { get; set; }
    public string NoteText { get; set; } = string.Empty;

    /// <summary>
    /// Who created this note (Phase 4.19: Actor system)
    /// </summary>
    public required Actor CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public bool IsPinned { get; set; }
}
