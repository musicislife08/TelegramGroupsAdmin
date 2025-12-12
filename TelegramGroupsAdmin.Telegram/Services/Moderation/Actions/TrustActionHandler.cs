using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Intents;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Results;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions;

/// <summary>
/// Handles trust intents by setting the user's trust flag in the database.
/// This is the domain expert for trusting users - it owns the trust state management.
/// </summary>
public class TrustActionHandler : IActionHandler<TrustIntent, TrustResult>
{
    private readonly ITelegramUserRepository _telegramUserRepository;
    private readonly ILogger<TrustActionHandler> _logger;

    public TrustActionHandler(
        ITelegramUserRepository telegramUserRepository,
        ILogger<TrustActionHandler> logger)
    {
        _telegramUserRepository = telegramUserRepository;
        _logger = logger;
    }

    public async Task<TrustResult> HandleAsync(TrustIntent intent, CancellationToken ct = default)
    {
        _logger.LogDebug(
            "Setting trust for user {UserId} by {Executor}",
            intent.UserId, intent.Executor.GetDisplayText());

        try
        {
            await _telegramUserRepository.UpdateTrustStatusAsync(
                intent.UserId,
                isTrusted: true,
                ct);

            _logger.LogInformation(
                "Trust set for user {UserId} globally",
                intent.UserId);

            return TrustResult.Succeeded();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set trust for user {UserId}", intent.UserId);
            return TrustResult.Failed(ex.Message);
        }
    }
}
