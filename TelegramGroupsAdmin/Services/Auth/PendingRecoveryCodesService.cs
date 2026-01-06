using System.Collections.Concurrent;
using TelegramGroupsAdmin.Constants;

namespace TelegramGroupsAdmin.Services.Auth;

/// <summary>
/// In-memory implementation for temporarily storing recovery codes during 2FA setup.
/// Codes are stored after TOTP verification succeeds and retrieved when the user
/// views the recovery codes page. This enables URL-based state flow in static SSR.
/// </summary>
public class PendingRecoveryCodesService : IPendingRecoveryCodesService
{
    /// <summary>
    /// Recovery codes expire after the same duration as the intermediate auth token.
    /// </summary>
    private static readonly TimeSpan CodeExpiration = AuthenticationConstants.IntermediateTokenLifetime;

    private readonly ConcurrentDictionary<string, PendingCodesData> _pendingCodes = new();
    private readonly ILogger<PendingRecoveryCodesService> _logger;

    public PendingRecoveryCodesService(ILogger<PendingRecoveryCodesService> logger)
    {
        _logger = logger;
    }

    public void StoreRecoveryCodes(string token, string userId, IReadOnlyList<string> recoveryCodes)
    {
        var data = new PendingCodesData(
            UserId: userId,
            RecoveryCodes: recoveryCodes,
            ExpiresAt: DateTimeOffset.UtcNow.Add(CodeExpiration)
        );

        _pendingCodes[token] = data;

        _logger.LogInformation(
            "Stored {Count} pending recovery codes for user {UserId}, expires at {ExpiresAt}",
            recoveryCodes.Count, userId, data.ExpiresAt);

        // Clean up expired entries (fire and forget)
        _ = Task.Run(() =>
        {
            try
            {
                CleanupExpiredEntries();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clean up expired pending recovery codes");
            }
        });
    }

    public IReadOnlyList<string>? RetrieveRecoveryCodes(string token, string userId)
    {
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("Recovery codes retrieval failed: null/empty token or userId");
            return null;
        }

        // Try to remove the entry (consume it - codes should only be shown once)
        if (!_pendingCodes.TryRemove(token, out var data))
        {
            _logger.LogWarning("Recovery codes retrieval failed: codes not found or already retrieved");
            return null;
        }

        // Check if expired
        if (data.ExpiresAt < DateTimeOffset.UtcNow)
        {
            _logger.LogWarning("Recovery codes retrieval failed: codes expired at {ExpiresAt}", data.ExpiresAt);
            return null;
        }

        // Check if userId matches
        if (data.UserId != userId)
        {
            _logger.LogWarning(
                "Recovery codes retrieval failed: userId mismatch. Expected {Expected}, got {Actual}",
                data.UserId, userId);
            return null;
        }

        _logger.LogInformation("Successfully retrieved pending recovery codes for user {UserId}", userId);
        return data.RecoveryCodes;
    }

    public bool HasRecoveryCodes(string token, string userId)
    {
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(userId))
        {
            return false;
        }

        if (!_pendingCodes.TryGetValue(token, out var data))
        {
            return false;
        }

        // Check expiration and userId match
        return data.ExpiresAt >= DateTimeOffset.UtcNow && data.UserId == userId;
    }

    private void CleanupExpiredEntries()
    {
        var now = DateTimeOffset.UtcNow;
        var expiredKeys = _pendingCodes
            .Where(kvp => kvp.Value.ExpiresAt < now)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _pendingCodes.TryRemove(key, out _);
        }

        if (expiredKeys.Count > 0)
        {
            _logger.LogDebug("Cleaned up {Count} expired pending recovery code entries", expiredKeys.Count);
        }
    }

    private record PendingCodesData(
        string UserId,
        IReadOnlyList<string> RecoveryCodes,
        DateTimeOffset ExpiresAt);
}
