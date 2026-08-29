namespace TelegramGroupsAdmin.Data.Constants;

/// <summary>
/// Keys for PostgreSQL advisory locks. The key space is global to the database, so every
/// advisory lock in the application must take its key from this file — that is what makes a
/// collision between two unrelated features visible in one place.
/// </summary>
public static class AdvisoryLockKeys
{
    /// <summary>Serializes ban celebration GIF rotation-cycle exhaustion.</summary>
    public const long BanCelebrationGifCycle = 7_201_001;

    /// <summary>Serializes ban celebration caption rotation-cycle exhaustion.</summary>
    public const long BanCelebrationCaptionCycle = 7_201_002;
}
