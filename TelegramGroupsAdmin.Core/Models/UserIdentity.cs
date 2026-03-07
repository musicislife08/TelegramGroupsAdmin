using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// Lightweight identity record for passing user display info through the moderation pipeline.
/// Constructed once at the call site from whatever source is available (SDK User, domain model, or DB fetch),
/// then flows through the entire handler chain — no handler needs to re-fetch from DB for logging.
/// </summary>
public sealed record UserIdentity(long Id, string? FirstName, string? LastName, string? Username)
{
    public string DisplayName { get; } = TelegramDisplayName.Format(FirstName, LastName, Username, Id);

    /// <summary>
    /// Creates an ID-only identity. Internal fallback used by FromAsync when user isn't in DB.
    /// </summary>
    public static UserIdentity FromId(long id) => new(id, null, null, null);
}
