using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions;

/// <inheritdoc />
public sealed class WelcomeCleanupHandler(
    IWelcomeResponsesRepository welcomeResponsesRepository,
    IBotModerationMessageHandler messageHandler,
    ILogger<WelcomeCleanupHandler> logger) : IWelcomeCleanupHandler
{
    public async Task<int> DeleteStrandedWelcomeMessagesAsync(
        UserIdentity user,
        ChatIdentity? chat,
        Actor executor,
        CancellationToken cancellationToken = default)
    {
        List<WelcomeResponse> responses;
        if (chat is null)
        {
            responses = await welcomeResponsesRepository.GetByUserAsync(user.Id, cancellationToken);
        }
        else
        {
            var single = await welcomeResponsesRepository.GetByUserAndChatAsync(
                user.Id, chat.Id, cancellationToken);
            responses = single is null ? [] : [single];
        }

        var deleted = 0;
        foreach (var response in responses)
        {
            if (response.WelcomeMessageId == 0)
                continue;

            var targetChat = chat ?? ChatIdentity.FromId(response.ChatId);

            try
            {
                // Idempotent: deleting an already-deleted message is a no-op at the API level.
                // DeleteAsync does not throw on failure — it reports it via DeleteResult.Success —
                // so this is the primary signal; the catch below is defense-in-depth only.
                var result = await messageHandler.DeleteAsync(
                    targetChat, response.WelcomeMessageId, executor, cancellationToken);

                if (result.Success)
                {
                    deleted++;
                    logger.LogDebug("Deleted stranded welcome message {MessageId} for {User} in {Chat}",
                        response.WelcomeMessageId, user.ToLogDebug(), targetChat.ToLogDebug());
                }
                else
                {
                    // Cleanup must never fail a ban that already landed on Telegram.
                    logger.LogDebug(
                        "Failed to delete stranded welcome message {MessageId} for {User} in {Chat}: {Error} (non-fatal)",
                        response.WelcomeMessageId, user.ToLogDebug(), targetChat.ToLogDebug(), result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                // Cleanup must never fail a ban that already landed on Telegram.
                logger.LogDebug(ex,
                    "Failed to delete stranded welcome message {MessageId} for {User} in {Chat} (non-fatal)",
                    response.WelcomeMessageId, user.ToLogDebug(), targetChat.ToLogDebug());
            }
        }

        if (deleted > 0)
        {
            logger.LogInformation("Welcome cleanup: deleted {Count} stranded welcome message(s) for {User}",
                deleted, user.ToLogInfo());
        }

        return deleted;
    }
}
