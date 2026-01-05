using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Results;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions;

/// <summary>
/// Domain handler for trust operations (trust, untrust).
/// Updates telegram_users.is_trusted flag (the source of truth).
/// Does NOT know about bans, warnings, or notifications (orchestrator composes those).
/// </summary>
public class TrustHandler : ITrustHandler
{
    private readonly ITelegramUserRepository _telegramUserRepository;
    private readonly ILogger<TrustHandler> _logger;

    public TrustHandler(
        ITelegramUserRepository telegramUserRepository,
        ILogger<TrustHandler> logger)
    {
        _telegramUserRepository = telegramUserRepository;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<TrustResult> TrustAsync(
        long userId,
        Actor executor,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        // Fetch once for logging
        var user = await _telegramUserRepository.GetByTelegramIdAsync(userId, cancellationToken);

        _logger.LogDebug(
            "Setting trust for user {User} by {Executor}",
            user.ToLogDebug(userId), executor.GetDisplayText());

        try
        {
            await _telegramUserRepository.UpdateTrustStatusAsync(
                userId,
                isTrusted: true,
                cancellationToken);

            _logger.LogInformation(
                "Trust set for {User} globally",
                user.ToLogInfo(userId));

            return TrustResult.Succeeded();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set trust for user {User}", user.ToLogDebug(userId));
            return TrustResult.Failed(ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<UntrustResult> UntrustAsync(
        long userId,
        Actor executor,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        // Fetch once for logging
        var user = await _telegramUserRepository.GetByTelegramIdAsync(userId, cancellationToken);

        _logger.LogDebug(
            "Removing trust for user {User} by {Executor}. Reason: {Reason}",
            user.ToLogDebug(userId), executor.GetDisplayText(), reason);

        try
        {
            // Use UpdateTrustStatusAsync for symmetry with TrustAsync
            // telegram_users.is_trusted is the source of truth
            await _telegramUserRepository.UpdateTrustStatusAsync(
                userId,
                isTrusted: false,
                cancellationToken);

            _logger.LogInformation(
                "Trust removed for {User} by {Executor}",
                user.ToLogInfo(userId), executor.GetDisplayText());

            return UntrustResult.Succeeded();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove trust for user {User}", user.ToLogDebug(userId));
            return UntrustResult.Failed(ex.Message);
        }
    }
}
