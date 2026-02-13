using System.Collections.Concurrent;
using System.Security.Cryptography;
using TelegramGroupsAdmin.Constants;

namespace TelegramGroupsAdmin.Services.Auth;

/// <summary>
/// In-memory implementation of intermediate authentication token management.
/// Tokens are stored with expiration and can only be used once.
/// </summary>
public class IntermediateAuthService : IIntermediateAuthService
{
    private readonly ConcurrentDictionary<string, TokenData> _tokens = new();
    private readonly ILogger<IntermediateAuthService> _logger;

    public IntermediateAuthService(ILogger<IntermediateAuthService> logger)
    {
        _logger = logger;
    }

    public string CreateToken(string userId, string? email = null)
    {
        // Generate cryptographically secure random token (32 bytes = 256 bits)
        var tokenBytes = RandomNumberGenerator.GetBytes(AuthenticationConstants.IntermediateTokenByteLength);
        var token = Convert.ToBase64String(tokenBytes);

        var tokenData = new TokenData(
            UserId: userId,
            Email: email,
            ExpiresAt: DateTimeOffset.UtcNow.Add(AuthenticationConstants.IntermediateTokenLifetime)
        );

        _tokens[token] = tokenData;

        _logger.LogInformation("Created intermediate auth token for {Email} ({UserId}), expires at {ExpiresAt}",
            tokenData.Email, userId, tokenData.ExpiresAt);

        // Clean up expired tokens (fire and forget)
        _ = Task.Run(() =>
        {
            try
            {
                CleanupExpiredTokens();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clean up expired intermediate auth tokens");
            }
        });

        return token;
    }

    public bool ValidateToken(string token, string userId)
    {
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("Token validation failed: null/empty token or userId");
            return false;
        }

        // Check if token exists without removing it
        if (!_tokens.TryGetValue(token, out var tokenData))
        {
            _logger.LogWarning("Token validation failed: token not found");
            return false;
        }

        // Check if token is expired
        if (tokenData.ExpiresAt < DateTimeOffset.UtcNow)
        {
            _logger.LogWarning("Token validation failed: token expired at {ExpiresAt}", tokenData.ExpiresAt);
            return false;
        }

        // Check if userId matches
        if (tokenData.UserId != userId)
        {
            _logger.LogWarning("Token validation failed: userId mismatch. Expected {Expected}, got {Actual}",
                tokenData.UserId, userId);
            return false;
        }

        return true;
    }

    public void ConsumeToken(string token)
    {
        if (_tokens.TryRemove(token, out _))
        {
            _logger.LogInformation("Token consumed successfully");
        }
    }

    public bool ValidateAndConsumeToken(string token, string userId)
    {
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("Token validation failed: null/empty token or userId");
            return false;
        }

        // Try to remove the token (consume it)
        if (!_tokens.TryRemove(token, out var tokenData))
        {
            _logger.LogWarning("Token validation failed: token not found or already consumed");
            return false;
        }

        // Check if token is expired
        if (tokenData.ExpiresAt < DateTimeOffset.UtcNow)
        {
            _logger.LogWarning("Token validation failed: token expired at {ExpiresAt}", tokenData.ExpiresAt);
            return false;
        }

        // Check if userId matches
        if (tokenData.UserId != userId)
        {
            _logger.LogWarning("Token validation failed: userId mismatch. Expected {Expected}, got {Actual}",
                tokenData.UserId, userId);
            return false;
        }

        _logger.LogInformation("Successfully validated and consumed token for user {UserId}", userId);
        return true;
    }

    private void CleanupExpiredTokens()
    {
        var now = DateTimeOffset.UtcNow;
        var expiredTokens = _tokens
            .Where(kvp => kvp.Value.ExpiresAt < now)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var token in expiredTokens)
        {
            _tokens.TryRemove(token, out _);
        }

        if (expiredTokens.Count > 0)
        {
            _logger.LogDebug("Cleaned up {Count} expired intermediate auth tokens", expiredTokens.Count);
        }
    }

    private record TokenData(string UserId, string? Email, DateTimeOffset ExpiresAt);
}
