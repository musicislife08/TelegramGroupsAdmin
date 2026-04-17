using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;

namespace TelegramGroupsAdmin.Telegram.Services.Welcome;

/// <summary>
/// Evaluates the three bypass rules in priority order: Telegram chat admin,
/// linked web admin, trusted user (toggle-gated). Returns the first match.
/// Singleton via <see cref="IServiceScopeFactory"/> for scoped-service access.
/// </summary>
public sealed class WelcomeBypassResolver(
    IServiceScopeFactory scopeFactory,
    ILogger<WelcomeBypassResolver> logger) : IWelcomeBypassResolver
{
    // Log format strings live here because they are only used by this class.
    private const string LogFormatChatAdmin =
        "Welcome bypass: {User} in {Chat} - Telegram chat admin/creator";
    private const string LogFormatWebAdmin =
        "Welcome bypass: {User} in {Chat} - linked web admin (level {Level})";
    private const string LogFormatTrusted =
        "Welcome bypass: {User} in {Chat} - trusted user, bypass enabled";

    public async Task<BypassDecision> ResolveAsync(
        UserIdentity user, ChatIdentity chat, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        // Rule 1: Telegram chat admin / creator (always on)
        var userService = sp.GetRequiredService<IBotUserService>();
        var chatMember = await userService.GetChatMemberAsync(chat.Id, user.Id, cancellationToken);
        if (chatMember.Status is ChatMemberStatus.Administrator or ChatMemberStatus.Creator)
        {
            logger.LogInformation(LogFormatChatAdmin, user.ToLogInfo(), chat.ToLogInfo());
            return BypassDecision.ChatAdmin;
        }

        // Rule 2: Linked web admin (always on)
        var mappingRepo = sp.GetRequiredService<ITelegramUserMappingRepository>();
        var permissionLevel = await mappingRepo.GetPermissionLevelByTelegramIdAsync(user.Id, cancellationToken);
        if (permissionLevel is (int)PermissionLevel.GlobalAdmin or (int)PermissionLevel.Owner)
        {
            logger.LogInformation(LogFormatWebAdmin, user.ToLogInfo(), chat.ToLogInfo(), permissionLevel);
            return BypassDecision.WebAdmin;
        }

        // Rule 3: Trusted user (toggle-gated)
        var configService = sp.GetRequiredService<IConfigService>();
        var config = await configService.GetEffectiveAsync<WelcomeConfig>(ConfigType.Welcome, chat.Id)
                     ?? new WelcomeConfig();
        if (!config.TrustedBypass.Enabled)
        {
            return BypassDecision.None;
        }

        var userRepo = sp.GetRequiredService<ITelegramUserRepository>();
        if (await userRepo.IsTrustedAsync(user.Id, cancellationToken))
        {
            logger.LogInformation(LogFormatTrusted, user.ToLogInfo(), chat.ToLogInfo());
            return BypassDecision.Trusted;
        }

        return BypassDecision.None;
    }
}
