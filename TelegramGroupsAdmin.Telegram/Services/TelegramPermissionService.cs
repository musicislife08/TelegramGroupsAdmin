using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.BotCommands;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <inheritdoc />
public sealed class TelegramPermissionService(
    ITelegramUserMappingRepository mappingRepository,
    IChatAdminsRepository chatAdminsRepository,
    ILogger<TelegramPermissionService> logger) : ITelegramPermissionService
{
    public async Task<PermissionLevel> GetEffectiveLevelAsync(long chatId, long telegramId, CancellationToken cancellationToken = default)
    {
        var webTier = await mappingRepository.GetPermissionLevelByTelegramIdAsync(telegramId, cancellationToken);
        var isChatAdmin = await chatAdminsRepository.IsAdminAsync(chatId, telegramId, cancellationToken);

        var effective = PermissionResolver.Resolve(webTier, isChatAdmin);
        logger.LogDebug(
            "Resolved permission for {TelegramId} in chat {ChatId}: {Tier} (web={WebTier}, chatAdmin={IsChatAdmin})",
            telegramId, chatId, effective, webTier, isChatAdmin);
        return effective;
    }
}
