using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SpamLibRequest = TelegramGroupsAdmin.SpamDetection.Models.SpamCheckRequest;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.SpamDetection.Models;
using TelegramGroupsAdmin.SpamDetection.Services;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Coordinates spam checking workflow by filtering trusted/admin users before spam detection
/// Centralizes spam checking logic so it's consistent between bot and UI
/// </summary>
public class SpamCheckCoordinator : ISpamCheckCoordinator
{
    private readonly ISpamDetectionEngine _spamDetectionEngine;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SpamCheckCoordinator> _logger;

    public SpamCheckCoordinator(
        ISpamDetectionEngine spamDetectionEngine,
        IServiceProvider serviceProvider,
        ILogger<SpamCheckCoordinator> logger)
    {
        _spamDetectionEngine = spamDetectionEngine;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<SpamCheckCoordinatorResult> CheckAsync(
        SpamLibRequest request,
        CancellationToken cancellationToken = default)
    {
        bool isUserTrusted = false;
        bool isUserAdmin = false;

        // Only check trust/admin status if UserId is provided and looks like a valid Telegram ID
        if (!string.IsNullOrWhiteSpace(request.UserId) && long.TryParse(request.UserId, out var userId))
        {
            using var scope = _serviceProvider.CreateScope();
            var userActionsRepository = scope.ServiceProvider.GetRequiredService<IUserActionsRepository>();

            // Check if user is explicitly trusted
            isUserTrusted = await userActionsRepository.IsUserTrustedAsync(
                userId,
                string.IsNullOrWhiteSpace(request.ChatId) ? null : long.Parse(request.ChatId),
                cancellationToken);

            // Check if user is a chat admin (only if ChatId provided)
            if (!string.IsNullOrWhiteSpace(request.ChatId) && long.TryParse(request.ChatId, out var chatId))
            {
                var chatAdminsRepository = scope.ServiceProvider.GetRequiredService<IChatAdminsRepository>();
                isUserAdmin = await chatAdminsRepository.IsAdminAsync(chatId, userId, cancellationToken);
            }
        }

        // If user is trusted or admin, skip spam detection entirely
        if (isUserTrusted || isUserAdmin)
        {
            var skipReason = isUserTrusted
                ? "User is trusted - spam detection bypassed"
                : "User is a chat admin - spam detection bypassed";

            _logger.LogInformation(
                "Skipping spam detection for user {UserId} in chat {ChatId}: {Reason}",
                request.UserId,
                request.ChatId ?? "N/A",
                skipReason);

            return new SpamCheckCoordinatorResult
            {
                IsUserTrusted = isUserTrusted,
                IsUserAdmin = isUserAdmin,
                SpamCheckSkipped = true,
                SkipReason = skipReason,
                SpamResult = null
            };
        }

        // Run spam detection
        _logger.LogDebug(
            "Running spam detection for user {UserId} in chat {ChatId}",
            request.UserId ?? "N/A",
            request.ChatId ?? "N/A");

        var spamResult = await _spamDetectionEngine.CheckMessageAsync(request, cancellationToken);

        return new SpamCheckCoordinatorResult
        {
            IsUserTrusted = false,
            IsUserAdmin = false,
            SpamCheckSkipped = false,
            SkipReason = null,
            SpamResult = spamResult
        };
    }
}
