using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Moderation;

namespace TelegramGroupsAdmin.Telegram.Services.Welcome;

/// <summary>
/// Centralized admission gate that checks all pending gates before restoring permissions.
/// Singleton — uses IServiceScopeFactory for scoped dependency access.
///
/// Gate checks:
/// 1. Profile scan gate — no pending ProfileScanAlert for user+chat
/// 2. Welcome gate — WelcomeResponse is Accepted (or null = welcome disabled = cleared)
///
/// User is admitted only when ALL gates are clear.
/// </summary>
public sealed class WelcomeAdmissionHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<WelcomeAdmissionHandler> logger) : IWelcomeAdmissionHandler
{
    public async Task<AdmissionResult> TryAdmitUserAsync(
        UserIdentity user,
        ChatIdentity chat,
        Actor executor,
        string reason,
        CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        // ── Gate 1: Profile scan ──
        var reportsRepo = sp.GetRequiredService<IReportsRepository>();
        var hasProfileHold = await reportsRepo.HasPendingProfileScanAlertAsync(user.Id, chat.Id, ct);

        if (hasProfileHold)
        {
            logger.LogDebug("Admission: {User} in {Chat} blocked by profile scan gate",
                user.ToLogDebug(), chat.ToLogDebug());
            return AdmissionResult.StillWaiting;
        }

        // ── Gate 2: Welcome response ──
        var welcomeRepo = sp.GetRequiredService<IWelcomeResponsesRepository>();
        var welcomeResponse = await welcomeRepo.GetByUserAndChatAsync(user.Id, chat.Id, ct);

        // null = no welcome response record = welcome disabled = gate cleared
        // Accepted = user completed welcome flow = gate cleared
        // Pending = user hasn't accepted yet = gate blocked
        if (welcomeResponse is { Response: WelcomeResponseType.Pending })
        {
            logger.LogDebug("Admission: {User} in {Chat} blocked by welcome gate (response pending)",
                user.ToLogDebug(), chat.ToLogDebug());
            return AdmissionResult.StillWaiting;
        }

        // ── All gates clear — restore permissions ──
        var moderationService = sp.GetRequiredService<IBotModerationService>();
        var intent = new RestorePermissionsIntent
        {
            User = user,
            Chat = chat,
            Executor = executor,
            Reason = reason
        };

        await moderationService.RestoreUserPermissionsAsync(intent, ct);

        logger.LogInformation("Admission: {User} admitted to {Chat} — all gates clear ({Reason})",
            user.ToLogInfo(), chat.ToLogInfo(), reason);

        return AdmissionResult.Admitted;
    }
}
