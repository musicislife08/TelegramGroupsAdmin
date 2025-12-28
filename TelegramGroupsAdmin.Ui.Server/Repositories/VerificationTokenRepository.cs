using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Telegram.Repositories.Mappings;
using TelegramGroupsAdmin.Data;
using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Ui.Server.Repositories;

public class VerificationTokenRepository : IVerificationTokenRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<VerificationTokenRepository> _logger;

    public VerificationTokenRepository(IDbContextFactory<AppDbContext> contextFactory, ILogger<VerificationTokenRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<long> CreateAsync(DataModels.VerificationTokenDto verificationToken, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        context.VerificationTokens.Add(verificationToken);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogDebug("Created verification token {Id} for user {UserId}, type {TokenType}",
            verificationToken.Id, verificationToken.UserId, verificationToken.TokenType);

        return verificationToken.Id;
    }

    public async Task<UiModels.VerificationToken?> GetByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.VerificationTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(vt => vt.Token == token, cancellationToken);

        return entity?.ToModel();
    }

    public async Task<UiModels.VerificationToken?> GetValidTokenAsync(string token, DataModels.TokenType tokenType, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var tokenTypeString = tokenType switch
        {
            DataModels.TokenType.EmailVerification => "email_verify",
            DataModels.TokenType.PasswordReset => "password_reset",
            DataModels.TokenType.EmailChange => "email_change",
            _ => throw new ArgumentException($"Unknown token type: {tokenType}")
        };

        var now = DateTimeOffset.UtcNow;

        var entity = await context.VerificationTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(vt =>
                vt.Token == token
                && vt.TokenTypeString == tokenTypeString
                && vt.UsedAt == null
                && vt.ExpiresAt > now, cancellationToken);

        return entity?.ToModel();
    }

    public async Task MarkAsUsedAsync(string token, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.VerificationTokens.FirstOrDefaultAsync(vt => vt.Token == token, cancellationToken);
        if (entity == null) return;

        entity.UsedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogDebug("Marked verification token as used: {Token}", token);
    }

    public async Task<int> CleanupExpiredAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;

        var expiredTokens = await context.VerificationTokens
            .Where(vt => vt.ExpiresAt <= now)
            .ToListAsync(cancellationToken);

        if (expiredTokens.Count > 0)
        {
            context.VerificationTokens.RemoveRange(expiredTokens);
            await context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Cleaned up {Count} expired verification tokens", expiredTokens.Count);
        }

        return expiredTokens.Count;
    }

    public async Task<int> DeleteByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var tokens = await context.VerificationTokens
            .Where(vt => vt.UserId == userId)
            .ToListAsync(cancellationToken);

        if (tokens.Count > 0)
        {
            context.VerificationTokens.RemoveRange(tokens);
            await context.SaveChangesAsync(cancellationToken);
            _logger.LogDebug("Deleted {Count} verification tokens for user {UserId}", tokens.Count, userId);
        }

        return tokens.Count;
    }
}
