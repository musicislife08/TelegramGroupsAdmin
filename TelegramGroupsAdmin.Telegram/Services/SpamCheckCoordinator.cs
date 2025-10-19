using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SpamLibRequest = TelegramGroupsAdmin.ContentDetection.Models.ContentCheckRequest;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Services;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Coordinates spam checking workflow by filtering trusted/admin users before spam detection
/// Centralizes spam checking logic so it's consistent between bot and UI
/// </summary>
public class SpamCheckCoordinator : ISpamCheckCoordinator
{
    private readonly IContentDetectionEngine _spamDetectionEngine;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SpamCheckCoordinator> _logger;

    public SpamCheckCoordinator(
        IContentDetectionEngine spamDetectionEngine,
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

        // Check trust/admin status for the user
        using var scope = _serviceProvider.CreateScope();
        var userActionsRepository = scope.ServiceProvider.GetRequiredService<IUserActionsRepository>();

        // Check if user is explicitly trusted
        isUserTrusted = await userActionsRepository.IsUserTrustedAsync(
            request.UserId,
            request.ChatId,
            cancellationToken);

        // Check if user is a chat admin
        var chatAdminsRepository = scope.ServiceProvider.GetRequiredService<IChatAdminsRepository>();
        isUserAdmin = await chatAdminsRepository.IsAdminAsync(request.ChatId, request.UserId, cancellationToken);

        // If user is trusted or admin, skip spam detection entirely
        if (isUserTrusted || isUserAdmin)
        {
            var skipReason = isUserTrusted
                ? "User is trusted - spam detection bypassed"
                : "User is a chat admin - spam detection bypassed";

            _logger.LogInformation(
                "Skipping spam detection for user {UserId} in chat {ChatId}: {Reason}",
                request.UserId,
                request.ChatId,
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
            request.UserId,
            request.ChatId);

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
