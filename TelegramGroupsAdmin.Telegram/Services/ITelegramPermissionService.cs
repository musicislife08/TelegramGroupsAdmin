using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Resolves a Telegram user's effective permission tier in a chat from the database at the moment
/// of the call (their active web-account tier combined with their current chat-admin status), so
/// slash commands and inline-button clicks are authorized against who the user is now, not who
/// they were when a button was sent.
/// </summary>
public interface ITelegramPermissionService
{
    Task<PermissionLevel> GetEffectiveLevelAsync(long chatId, long telegramId, CancellationToken cancellationToken = default);
}
