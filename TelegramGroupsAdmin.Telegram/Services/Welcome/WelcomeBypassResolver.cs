using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services.Welcome;

/// <summary>
/// Evaluates the three bypass rules in priority order: Telegram chat admin (any tracked chat),
/// linked web admin (GlobalAdmin/Owner), trusted user (toggle-gated). Returns the first match
/// as a <see cref="BypassResolution"/> that carries both the decision and a human-readable
/// reason string suitable for audit persistence. Singleton via <see cref="IServiceScopeFactory"/>
/// for scoped-service access.
/// </summary>
public sealed class WelcomeBypassResolver(
    IServiceScopeFactory scopeFactory,
    ILogger<WelcomeBypassResolver> logger) : IWelcomeBypassResolver
{
    // Log format strings live here because they are only used by this class.
    // Split per rule so each bypass path has its own forensic log entry.
    private const string AdminBypassChatAdminFormat =
        "Bypass: {User} is chat admin in {AdminChatCount} tracked chats (joining {Chat})";
    private const string AdminBypassWebAdminFormat =
        "Bypass: {User} is linked web admin ({Level}) (joining {Chat})";
    private const string TrustedBypassFormat =
        "Bypass: {User} is trusted, per-chat toggle enabled (joining {Chat})";

    public async Task<BypassResolution> ResolveAsync(
        UserIdentity user, ChatIdentity chat, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        // Rule 1: Telegram chat admin (creator or administrator) in any tracked chat.
        var chatAdminsRepo = sp.GetRequiredService<IChatAdminsRepository>();
        var adminChats = await chatAdminsRepo.GetAdminChatsAsync(user.Id, cancellationToken);
        if (adminChats.Count > 0)
        {
            logger.LogDebug(AdminBypassChatAdminFormat,
                user.ToLogDebug(), adminChats.Count, chat.ToLogDebug());
            return new BypassResolution(
                BypassDecision.Admin,
                $"Telegram chat admin ({adminChats.Count} chats)");
        }

        // Rule 1 (cont.): Linked web admin at GlobalAdmin or Owner permission level.
        var mappingRepo = sp.GetRequiredService<ITelegramUserMappingRepository>();
        var permissionLevel = await mappingRepo.GetPermissionLevelByTelegramIdAsync(user.Id, cancellationToken);
        if (permissionLevel >= PermissionLevel.GlobalAdmin)
        {
            logger.LogDebug(AdminBypassWebAdminFormat,
                user.ToLogDebug(), permissionLevel, chat.ToLogDebug());
            return new BypassResolution(
                BypassDecision.Admin,
                $"Linked web admin ({permissionLevel})");
        }

        // Rule 2: Trusted user (toggle-gated).
        var configService = sp.GetRequiredService<IConfigService>();
        var config = await configService.GetEffectiveAsync<WelcomeConfig>(ConfigType.Welcome, chat.Id);
        if (config?.TrustedBypass.Enabled == true)
        {
            var userRepo = sp.GetRequiredService<ITelegramUserRepository>();
            if (await userRepo.IsTrustedAsync(user.Id, cancellationToken))
            {
                logger.LogDebug(TrustedBypassFormat, user.ToLogDebug(), chat.ToLogDebug());
                return new BypassResolution(BypassDecision.Trusted, "Trusted user");
            }
        }

        return BypassResolution.None();
    }
}
