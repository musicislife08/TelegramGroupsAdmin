namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// Lightweight identity record for passing chat display info through the moderation pipeline.
/// Constructed once at the call site from whatever source is available (SDK Chat, domain model, or DB fetch),
/// then flows through the entire handler chain — no handler needs to re-fetch from DB for logging.
/// </summary>
public record ChatIdentity(long Id, string? ChatName)
{
    /// <summary>
    /// Creates an ID-only identity. Internal fallback used by FromAsync when chat isn't in DB.
    /// </summary>
    public static ChatIdentity FromId(long id) => new(id, null);
}
