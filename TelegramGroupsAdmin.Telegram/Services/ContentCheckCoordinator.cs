using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SpamLibRequest = TelegramGroupsAdmin.ContentDetection.Models.ContentCheckRequest;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.ContentDetection.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Coordinates content checking workflow by filtering trusted/admin users before content detection
/// Supports "always-run" critical checks that bypass trust/admin status (Phase 4.14)
/// Centralizes checking logic so it's consistent between bot and UI
/// </summary>
public class ContentCheckCoordinator : IContentCheckCoordinator
{
    private readonly IContentDetectionEngine _spamDetectionEngine;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ContentCheckCoordinator> _logger;

    public ContentCheckCoordinator(
        IContentDetectionEngine spamDetectionEngine,
        IServiceProvider serviceProvider,
        ILogger<ContentCheckCoordinator> logger)
    {
        _spamDetectionEngine = spamDetectionEngine;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<ContentCheckCoordinatorResult> CheckAsync(
        SpamLibRequest request,
        CancellationToken cancellationToken = default)
    {
        bool isUserTrusted = false;
        bool isUserAdmin = false;

        // Check trust/admin status for the user
        using var scope = _serviceProvider.CreateScope();
        var userActionsRepository = scope.ServiceProvider.GetRequiredService<IUserActionsRepository>();
        var chatAdminsRepository = scope.ServiceProvider.GetRequiredService<IChatAdminsRepository>();
        var contentCheckConfigRepo = scope.ServiceProvider.GetRequiredService<IContentCheckConfigRepository>();

        // Check if user is explicitly trusted
        isUserTrusted = await userActionsRepository.IsUserTrustedAsync(
            request.UserId,
            request.ChatId,
            cancellationToken);

        // Check if user is a chat admin
        isUserAdmin = await chatAdminsRepository.IsAdminAsync(request.ChatId, request.UserId, cancellationToken);

        // Phase 4.14: 2-Phase Detection
        // Phase 1: Get list of critical checks (always_run=true) for this chat
        var criticalChecks = await contentCheckConfigRepo.GetCriticalChecksAsync(request.ChatId, cancellationToken);
        var criticalCheckNames = criticalChecks
            .Where(c => c.Enabled && c.AlwaysRun)
            .Select(c => c.CheckName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _logger.LogDebug(
            "Chat {ChatId} has {Count} critical checks: {Checks}",
            request.ChatId,
            criticalCheckNames.Count,
            string.Join(", ", criticalCheckNames));

        // PERF-3 Option A: Early exit if no critical checks and user is trusted/admin
        // Avoids expensive spam detection operations (DB queries, Bayes training, TF-IDF vectorization)
        // for trusted users when no critical checks are configured
        if (criticalCheckNames.Count == 0 && (isUserTrusted || isUserAdmin))
        {
            var skipReason = isUserTrusted
                ? "User is trusted and no critical checks configured"
                : "User is admin and no critical checks configured";

            _logger.LogInformation(
                "Skipping all spam detection for user {UserId} in chat {ChatId}: {Reason}",
                request.UserId,
                request.ChatId,
                skipReason);

            return new ContentCheckCoordinatorResult
            {
                IsUserTrusted = isUserTrusted,
                IsUserAdmin = isUserAdmin,
                SpamCheckSkipped = true,
                SkipReason = skipReason,
                CriticalCheckViolations = [],
                SpamResult = null
            };
        }

        // Phase 2: Run all content checks
        _logger.LogDebug(
            "Running content detection for user {UserId} in chat {ChatId} (Trusted: {Trusted}, Admin: {Admin})",
            request.UserId,
            request.ChatId,
            isUserTrusted,
            isUserAdmin);

        // PERF-3 Option B: Pass trust context to individual checks
        // Allows non-critical checks (Bayes, Similarity, OpenAI) to skip expensive operations for trusted/admin users
        var enrichedRequest = request with
        {
            IsUserTrusted = isUserTrusted,
            IsUserAdmin = isUserAdmin
        };

        var fullResult = await _spamDetectionEngine.CheckMessageAsync(enrichedRequest, cancellationToken);

        // Phase 3: Separate critical violations from regular spam results
        var criticalViolations = new List<string>();

        if (criticalCheckNames.Count > 0 && fullResult.CheckResults != null)
        {
            // Check if any critical checks flagged violations
            foreach (var checkResult in fullResult.CheckResults)
            {
                if (criticalCheckNames.Contains(checkResult.CheckName.ToString()) &&
                    checkResult.Result == CheckResultType.Spam)
                {
                    criticalViolations.Add($"{checkResult.CheckName}: {checkResult.Details}");

                    _logger.LogWarning(
                        "Critical check violation: {CheckName} flagged user {UserId} in chat {ChatId}: {Details}",
                        checkResult.CheckName,
                        request.UserId,
                        request.ChatId,
                        checkResult.Details);
                }
            }
        }

        // Phase 4: Determine if regular spam checks should be honored
        bool shouldSkipRegularChecks = (isUserTrusted || isUserAdmin) && criticalViolations.Count == 0;

        if (shouldSkipRegularChecks)
        {
            var skipReason = isUserTrusted
                ? "User is trusted - regular spam detection bypassed (critical checks passed)"
                : "User is a chat admin - regular spam detection bypassed (critical checks passed)";

            _logger.LogInformation(
                "Skipping regular spam detection for user {UserId} in chat {ChatId}: {Reason}",
                request.UserId,
                request.ChatId,
                skipReason);

            return new ContentCheckCoordinatorResult
            {
                IsUserTrusted = isUserTrusted,
                IsUserAdmin = isUserAdmin,
                SpamCheckSkipped = true,
                SkipReason = skipReason,
                CriticalCheckViolations = criticalViolations,
                SpamResult = null  // Regular checks not evaluated
            };
        }

        // Return full results (either untrusted user, or trusted/admin user with critical violations)
        return new ContentCheckCoordinatorResult
        {
            IsUserTrusted = isUserTrusted,
            IsUserAdmin = isUserAdmin,
            SpamCheckSkipped = false,
            SkipReason = null,
            CriticalCheckViolations = criticalViolations,
            SpamResult = fullResult
        };
    }
}
