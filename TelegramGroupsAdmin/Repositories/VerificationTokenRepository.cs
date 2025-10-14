using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Models;

namespace TelegramGroupsAdmin.Repositories;

public class VerificationTokenRepository
{
    private readonly AppDbContext _context;
    private readonly ILogger<VerificationTokenRepository> _logger;

    public VerificationTokenRepository(AppDbContext context, ILogger<VerificationTokenRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<long> CreateAsync(DataModels.VerificationToken verificationToken, CancellationToken ct = default)
    {
        _context.VerificationTokens.Add(verificationToken);
        await _context.SaveChangesAsync(ct);

        _logger.LogDebug("Created verification token {Id} for user {UserId}, type {TokenType}",
            verificationToken.Id, verificationToken.UserId, verificationToken.TokenType);

        return verificationToken.Id;
    }

    public async Task<UiModels.VerificationToken?> GetByTokenAsync(string token, CancellationToken ct = default)
    {
        var entity = await _context.VerificationTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(vt => vt.Token == token, ct);

        return entity?.ToUiModel();
    }

    public async Task<UiModels.VerificationToken?> GetValidTokenAsync(string token, DataModels.TokenType tokenType, CancellationToken ct = default)
    {
        var tokenTypeString = tokenType switch
        {
            DataModels.TokenType.EmailVerification => "email_verify",
            DataModels.TokenType.PasswordReset => "password_reset",
            DataModels.TokenType.EmailChange => "email_change",
            _ => throw new ArgumentException($"Unknown token type: {tokenType}")
        };

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var entity = await _context.VerificationTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(vt =>
                vt.Token == token
                && vt.TokenTypeString == tokenTypeString
                && vt.UsedAt == null
                && vt.ExpiresAt > now, ct);

        return entity?.ToUiModel();
    }

    public async Task MarkAsUsedAsync(string token, CancellationToken ct = default)
    {
        var entity = await _context.VerificationTokens.FirstOrDefaultAsync(vt => vt.Token == token, ct);
        if (entity == null) return;

        entity.UsedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await _context.SaveChangesAsync(ct);

        _logger.LogDebug("Marked verification token as used: {Token}", token);
    }

    public async Task<int> CleanupExpiredAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var expiredTokens = await _context.VerificationTokens
            .Where(vt => vt.ExpiresAt <= now)
            .ToListAsync(ct);

        if (expiredTokens.Count > 0)
        {
            _context.VerificationTokens.RemoveRange(expiredTokens);
            await _context.SaveChangesAsync(ct);
            _logger.LogInformation("Cleaned up {Count} expired verification tokens", expiredTokens.Count);
        }

        return expiredTokens.Count;
    }

    public async Task<int> DeleteByUserIdAsync(string userId, CancellationToken ct = default)
    {
        var tokens = await _context.VerificationTokens
            .Where(vt => vt.UserId == userId)
            .ToListAsync(ct);

        if (tokens.Count > 0)
        {
            _context.VerificationTokens.RemoveRange(tokens);
            await _context.SaveChangesAsync(ct);
            _logger.LogDebug("Deleted {Count} verification tokens for user {UserId}", tokens.Count, userId);
        }

        return tokens.Count;
    }
}
