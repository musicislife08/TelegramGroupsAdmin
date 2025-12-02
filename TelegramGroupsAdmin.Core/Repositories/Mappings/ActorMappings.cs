using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Core.Repositories.Mappings;

/// <summary>
/// Actor conversion helper methods for exclusive arc pattern (Phase 4.19)
/// </summary>
public static class ActorMappings
{
    // ============================================================================
    // Actor Conversion Helpers (Phase 4.19)
    // ============================================================================

    /// <summary>
    /// Convert database actor columns (exclusive arc) to Actor model
    /// Exactly one of the three parameters must be non-null (enforced by DB CHECK constraint)
    /// </summary>
    public static Actor ToActor(
        string? webUserId,
        long? telegramUserId,
        string? systemIdentifier,
        string? webUserEmail = null,
        string? telegramUsername = null,
        string? telegramFirstName = null,
        string? telegramLastName = null)
    {
        if (webUserId != null)
        {
            return Actor.FromWebUser(webUserId, webUserEmail);
        }

        if (telegramUserId != null)
        {
            return Actor.FromTelegramUser(telegramUserId.Value, telegramUsername, telegramFirstName, telegramLastName);
        }

        if (systemIdentifier != null)
        {
            return Actor.FromSystem(systemIdentifier);
        }

        // Should never happen due to CHECK constraint, but handle gracefully
        return Actor.Unknown;
    }

    /// <summary>
    /// Populate DTO actor columns from Actor model (for ToDto methods)
    /// </summary>
    public static void SetActorColumns(
        Actor actor,
        out string? webUserId,
        out long? telegramUserId,
        out string? systemIdentifier)
    {
        webUserId = actor.GetWebUserId();
        telegramUserId = actor.GetTelegramUserId();
        systemIdentifier = actor.GetSystemIdentifier();
    }
}
