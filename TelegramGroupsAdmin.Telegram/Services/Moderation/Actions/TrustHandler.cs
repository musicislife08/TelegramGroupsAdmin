using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Extensions;
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
        UserIdentity user,
        Actor executor,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Setting trust for user {User} by {Executor}",
            user.ToLogDebug(), executor.GetDisplayText());

        try
        {
            await _telegramUserRepository.UpdateTrustStatusAsync(
                user.Id,
                isTrusted: true,
                cancellationToken);

            _logger.LogInformation(
                "Trust set for {User} globally",
                user.ToLogInfo());

            return TrustResult.Succeeded();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set trust for user {User}", user.ToLogDebug());
            return TrustResult.Failed(ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<UntrustResult> UntrustAsync(
        UserIdentity user,
        Actor executor,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Removing trust for user {User} by {Executor}. Reason: {Reason}",
            user.ToLogDebug(), executor.GetDisplayText(), reason);

        try
        {
            // Use UpdateTrustStatusAsync for symmetry with TrustAsync
            // telegram_users.is_trusted is the source of truth
            await _telegramUserRepository.UpdateTrustStatusAsync(
                user.Id,
                isTrusted: false,
                cancellationToken);

            _logger.LogInformation(
                "Trust removed for {User} by {Executor}",
                user.ToLogInfo(), executor.GetDisplayText());

            return UntrustResult.Succeeded();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove trust for user {User}", user.ToLogDebug());
            return UntrustResult.Failed(ex.Message);
        }
    }
}
