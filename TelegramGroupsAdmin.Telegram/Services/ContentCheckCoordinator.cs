using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SpamLibRequest = TelegramGroupsAdmin.ContentDetection.Models.ContentCheckRequest;
using TelegramGroupsAdmin.Core;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Configuration.Services;

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
        // CRITICAL: Early exit for Telegram system accounts (777000, 1087968824, etc.)
        // System accounts are used for channel posts, anonymous admin posts, etc.
        // Must bypass ALL checks (including database queries) to avoid race condition
        // on first message before user record is created
        if (TelegramConstants.IsSystemUser(request.UserId))
        {
            _logger.LogInformation(
                "Skipping all content detection for Telegram system account ({User}) in {Chat}",
                request.UserName,
                request.ChatName);

            return new ContentCheckCoordinatorResult
            {
                IsUserTrusted = true, // System accounts are always trusted
                IsUserAdmin = false,
                SpamCheckSkipped = true,
                SkipReason = "Telegram system account (channel posts, anonymous admins, etc.) - always trusted",
                CriticalCheckViolations = [],
                SpamResult = null
            };
        }

        bool isUserTrusted = false;
        bool isUserAdmin = false;

        // Check trust/admin status for the user
        using var scope = _serviceProvider.CreateScope();
        var userRepository = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
        var chatAdminsRepository = scope.ServiceProvider.GetRequiredService<IChatAdminsRepository>();
        var configService = scope.ServiceProvider.GetRequiredService<IConfigService>();

        // REFACTOR-5: Check if user is explicitly trusted (source of truth: telegram_users.is_trusted)
        isUserTrusted = await userRepository.IsTrustedAsync(
            request.UserId,
            cancellationToken);

        // Check if user is a chat admin
        isUserAdmin = await chatAdminsRepository.IsAdminAsync(request.ChatId, request.UserId, cancellationToken);

        _logger.LogInformation(
            "{User} status in {Chat}: Trusted={Trusted}, Admin={Admin}",
            request.UserName,
            request.ChatName,
            isUserTrusted,
            isUserAdmin);

        // Phase 4.14: 2-Phase Detection
        // Phase 1: Get list of critical checks (always_run=true) using optimized JSONB query
        var criticalCheckNames = await configService.GetCriticalCheckNamesAsync(request.ChatId, cancellationToken);

        _logger.LogInformation(
            "{Chat} has {Count} critical (always_run) checks configured: {Checks}",
            request.ChatName,
            criticalCheckNames.Count,
            criticalCheckNames.Count > 0 ? string.Join(", ", criticalCheckNames) : "none");

        // PERF-3 Option A: Early exit if no critical checks and user is trusted/admin
        // Avoids expensive spam detection operations (DB queries, Bayes training, TF-IDF vectorization)
        // for trusted users when no critical checks are configured
        if (criticalCheckNames.Count == 0 && (isUserTrusted || isUserAdmin))
        {
            var skipReason = isUserTrusted
                ? "User is trusted and no critical checks configured"
                : "User is admin and no critical checks configured";

            _logger.LogInformation(
                "Skipping all spam detection for {User} in {Chat}: {Reason}",
                request.UserName,
                request.ChatName,
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
        var detectionReason = criticalCheckNames.Count > 0
            ? $"{criticalCheckNames.Count} critical checks require scanning"
            : isUserTrusted || isUserAdmin
                ? "untrusted/non-admin user"
                : "standard user";

        _logger.LogInformation(
            "Running full spam detection pipeline for {User} in {Chat}: {Reason}",
            request.UserName,
            request.ChatName,
            detectionReason);

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
                        "Critical check violation: {CheckName} flagged {User} in {Chat}: {Details}",
                        checkResult.CheckName,
                        request.UserName,
                        request.ChatName,
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
                "✓ Critical checks passed, skipping regular spam detection for {User} in {Chat}: {Reason}",
                request.UserName,
                request.ChatName,
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
