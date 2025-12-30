using System.Security.Cryptography;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for managing Telegram account linking operations.
/// </summary>
public class TelegramLinkService(
    ITelegramLinkTokenRepository linkTokenRepository,
    ITelegramUserMappingRepository mappingRepository) : ITelegramLinkService
{
    /// <inheritdoc />
    public async Task<TelegramLinkTokenRecord> GenerateLinkTokenAsync(string userId, CancellationToken cancellationToken = default)
    {
        // Revoke any existing unused tokens for this user
        await linkTokenRepository.RevokeUnusedTokensForUserAsync(userId, cancellationToken);

        // Generate new token using URL-safe Base64
        // 9 bytes = 72 bits of entropy = exactly 12 Base64 characters (no padding)
        var tokenBytes = new byte[9];
        RandomNumberGenerator.Fill(tokenBytes);
        var token = Convert.ToBase64String(tokenBytes)
            .Replace('+', '-')
            .Replace('/', '_');

        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(15);

        var tokenRecord = new TelegramLinkTokenRecord(
            Token: token,
            UserId: userId,
            CreatedAt: now,
            ExpiresAt: expiresAt,
            UsedAt: null,
            UsedByTelegramId: null
        );

        await linkTokenRepository.InsertAsync(tokenRecord, cancellationToken);

        return tokenRecord;
    }

    /// <inheritdoc />
    public async Task<bool> UnlinkAccountAsync(long mappingId, string userId, CancellationToken cancellationToken = default)
    {
        // Get all mappings for this user to verify ownership
        var userMappings = await mappingRepository.GetByUserIdAsync(userId, cancellationToken);

        // Check if the mapping belongs to this user
        var mapping = userMappings.FirstOrDefault(m => m.Id == mappingId);
        if (mapping == null)
        {
            // Mapping doesn't exist or doesn't belong to this user
            return false;
        }

        // Deactivate the mapping
        return await mappingRepository.DeactivateAsync(mappingId, cancellationToken);
    }
}
