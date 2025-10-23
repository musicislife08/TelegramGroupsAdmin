namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Moderation queue statistics
/// </summary>
public class ModerationQueueStats
{
    public int BannedCount { get; set; }
    public int TaggedCount { get; set; }
    public int WarnedCount { get; set; }
    public int NotesCount { get; set; }  // Future: Phase 4
}
