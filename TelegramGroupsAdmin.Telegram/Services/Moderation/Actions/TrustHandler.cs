using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Models;
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
        CancellationToken ct = default)
    {
        _logger.LogDebug(
            "Setting trust for user {UserId} by {Executor}",
            userId, executor.GetDisplayText());

        try
        {
            await _telegramUserRepository.UpdateTrustStatusAsync(
                userId,
                isTrusted: true,
                ct);

            _logger.LogInformation(
                "Trust set for user {UserId} globally",
                userId);

            return TrustResult.Succeeded();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set trust for user {UserId}", userId);
            return TrustResult.Failed(ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<UntrustResult> UntrustAsync(
        long userId,
        Actor executor,
        string? reason,
        CancellationToken ct = default)
    {
        _logger.LogDebug(
            "Removing trust for user {UserId} by {Executor}. Reason: {Reason}",
            userId, executor.GetDisplayText(), reason);

        try
        {
            // Use UpdateTrustStatusAsync for symmetry with TrustAsync
            // telegram_users.is_trusted is the source of truth
            await _telegramUserRepository.UpdateTrustStatusAsync(
                userId,
                isTrusted: false,
                ct);

            _logger.LogInformation(
                "Trust removed for user {UserId} by {Executor}",
                userId, executor.GetDisplayText());

            return UntrustResult.Succeeded();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove trust for user {UserId}", userId);
            return UntrustResult.Failed(ex.Message);
        }
    }
}
