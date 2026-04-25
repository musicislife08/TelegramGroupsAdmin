namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// Lightweight identity record for passing chat display info through the moderation pipeline.
/// Constructed once at the call site from whatever source is available (SDK Chat, domain model, or DB fetch),
/// then flows through the entire handler chain — no handler needs to re-fetch from DB for logging.
/// </summary>
/// <remarks>
/// Lives in the Data project so both Core and Configuration can use it without
/// circular project references during the Configuration restoration refactor.
/// Final home (Core) is restored in Task 5 once Core ↔ Configuration edges are flipped.
/// </remarks>
public sealed record ChatIdentity(long Id, string? ChatName)
{
    public string DisplayName { get; } = ChatName ?? $"Chat {Id}";

    /// <summary>
    /// Creates an ID-only identity. Internal fallback used by FromAsync when chat isn't in DB.
    /// </summary>
    public static ChatIdentity FromId(long id) => new(id, null);
}
