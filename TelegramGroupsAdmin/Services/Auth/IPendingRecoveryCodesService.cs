namespace TelegramGroupsAdmin.Services.Auth;

/// <summary>
/// Manages temporary storage of recovery codes during 2FA setup.
/// Recovery codes are stored after TOTP verification and retrieved once
/// when the user views them. This enables URL-based state in static SSR.
/// </summary>
public interface IPendingRecoveryCodesService
{
    /// <summary>
    /// Stores recovery codes temporarily, keyed by the intermediate auth token.
    /// Codes expire after a short period and can only be retrieved once.
    /// </summary>
    /// <param name="token">The intermediate authentication token (used as key)</param>
    /// <param name="userId">The user ID (for validation)</param>
    /// <param name="recoveryCodes">The recovery codes to store</param>
    void StoreRecoveryCodes(string token, string userId, IReadOnlyList<string> recoveryCodes);

    /// <summary>
    /// Retrieves and removes stored recovery codes for the given token.
    /// Returns null if codes don't exist, are expired, or userId doesn't match.
    /// </summary>
    /// <param name="token">The intermediate authentication token</param>
    /// <param name="userId">The user ID (must match stored userId)</param>
    /// <returns>The recovery codes, or null if not found/expired/invalid</returns>
    IReadOnlyList<string>? RetrieveRecoveryCodes(string token, string userId);

    /// <summary>
    /// Checks if recovery codes exist for the given token without consuming them.
    /// </summary>
    /// <param name="token">The intermediate authentication token</param>
    /// <param name="userId">The user ID (must match stored userId)</param>
    /// <returns>True if recovery codes are available</returns>
    bool HasRecoveryCodes(string token, string userId);
}
