using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Services.UserApi;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Send/edit messages via WTelegram user API — messages appear from admin's personal Telegram account.
/// Parallel to <see cref="WebBotMessagingService"/> which sends via bot API with signature.
/// </summary>
public class WebUserMessagingService(
    ITelegramSessionManager sessionManager,
    ILogger<WebUserMessagingService> logger) : IWebUserMessagingService
{
    public async Task<WebUserFeatureAvailability> CheckFeatureAvailabilityAsync(
        WebUserIdentity webUser,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = await sessionManager.GetClientAsync(webUser.Id, cancellationToken);
            if (client is null)
            {
                return new WebUserFeatureAvailability(false, "No connected Telegram account. Connect in Settings → Telegram.");
            }

            logger.LogDebug("User API messaging available for {User}", webUser.ToLogDebug());
            return new WebUserFeatureAvailability(true, null);
        }
        catch (TelegramFloodWaitException ex)
        {
            logger.LogWarning("Flood wait checking user API availability for {User}: {Seconds}s", webUser.ToLogDebug(), ex.WaitSeconds);
            return new WebUserFeatureAvailability(false, $"Rate limited — try again in {ex.WaitSeconds}s");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to check user API availability for {User}", webUser.ToLogDebug());
            return new WebUserFeatureAvailability(false, $"Error: {ex.Message}");
        }
    }

    public async Task<WebUserChatAvailability> CanSendToChatAsync(
        WebUserIdentity webUser,
        long chatId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = await sessionManager.GetClientAsync(webUser.Id, cancellationToken);
            if (client is null)
            {
                return new WebUserChatAvailability(false, "No connected Telegram account");
            }

            var peer = client.GetInputPeerForChat(chatId);
            if (peer is not null)
            {
                return new WebUserChatAvailability(true, null);
            }

            // Peer not found — try refreshing the cache once (admin may have joined since connect)
            await client.WarmPeerCacheAsync();
            peer = client.GetInputPeerForChat(chatId);

            return peer is not null
                ? new WebUserChatAvailability(true, null)
                : new WebUserChatAvailability(false, "You're not a member of this group with your personal account");
        }
        catch (TelegramFloodWaitException ex)
        {
            logger.LogWarning("Flood wait checking chat availability: {Seconds}s", ex.WaitSeconds);
            return new WebUserChatAvailability(false, $"Rate limited — try again in {ex.WaitSeconds}s");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to check chat availability for {User} in chat {ChatId}", webUser.ToLogDebug(), chatId);
            return new WebUserChatAvailability(false, $"Error: {ex.Message}");
        }
    }

    public async Task<WebUserMessageResult> SendMessageAsync(
        WebUserIdentity webUser,
        long chatId,
        string text,
        long? replyToMessageId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text))
                return new WebUserMessageResult(false, "Message text cannot be empty");

            if (text.Length > 4096)
                return new WebUserMessageResult(false, $"Message too long. Maximum is 4096 characters (current: {text.Length}).");

            var client = await sessionManager.GetClientAsync(webUser.Id, cancellationToken);
            if (client is null)
                return new WebUserMessageResult(false, "No connected Telegram account");

            var peer = client.GetInputPeerForChat(chatId);
            if (peer is null)
            {
                // Try refreshing cache once
                await client.WarmPeerCacheAsync();
                peer = client.GetInputPeerForChat(chatId);
                if (peer is null)
                    return new WebUserMessageResult(false, "You're not a member of this group");
            }

            var replyTo = replyToMessageId.HasValue ? (int)replyToMessageId.Value : 0;
            var sentMessage = await client.SendMessageAsync(peer, text, replyTo);

            logger.LogDebug(
                "Sent user message {MessageId} as {User} in chat {ChatId}",
                sentMessage.ID, webUser.ToLogDebug(), chatId);

            return new WebUserMessageResult(true, null);
        }
        catch (TelegramFloodWaitException ex)
        {
            logger.LogWarning("Flood wait sending message for {User}: {Seconds}s", webUser.ToLogDebug(), ex.WaitSeconds);
            return new WebUserMessageResult(false, $"Rate limited — try again in {ex.WaitSeconds}s");
        }
        catch (TL.RpcException ex)
        {
            logger.LogWarning(ex, "Telegram API error sending message for {User} in chat {ChatId}: {Error}",
                webUser.ToLogDebug(), chatId, ex.Message);
            return new WebUserMessageResult(false, $"Telegram error: {ex.Message}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send user message for {User} in chat {ChatId}", webUser.ToLogDebug(), chatId);
            return new WebUserMessageResult(false, ex.Message);
        }
    }

    public async Task<WebUserMessageResult> EditMessageAsync(
        WebUserIdentity webUser,
        long chatId,
        int messageId,
        string text,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text))
                return new WebUserMessageResult(false, "Message text cannot be empty");

            if (text.Length > 4096)
                return new WebUserMessageResult(false, $"Message too long. Maximum is 4096 characters (current: {text.Length}).");

            var client = await sessionManager.GetClientAsync(webUser.Id, cancellationToken);
            if (client is null)
                return new WebUserMessageResult(false, "No connected Telegram account");

            var peer = client.GetInputPeerForChat(chatId);
            if (peer is null)
                return new WebUserMessageResult(false, "You're not a member of this group");

            await client.Messages_EditMessage(peer, messageId, text);

            logger.LogDebug(
                "Edited user message {MessageId} as {User} in chat {ChatId}",
                messageId, webUser.ToLogDebug(), chatId);

            return new WebUserMessageResult(true, null);
        }
        catch (TelegramFloodWaitException ex)
        {
            logger.LogWarning("Flood wait editing message for {User}: {Seconds}s", webUser.ToLogDebug(), ex.WaitSeconds);
            return new WebUserMessageResult(false, $"Rate limited — try again in {ex.WaitSeconds}s");
        }
        catch (TL.RpcException ex)
        {
            logger.LogWarning(ex, "Telegram API error editing message {MessageId} for {User}: {Error}",
                messageId, webUser.ToLogDebug(), ex.Message);
            return new WebUserMessageResult(false, $"Telegram error: {ex.Message}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to edit user message {MessageId} for {User}", messageId, webUser.ToLogDebug());
            return new WebUserMessageResult(false, ex.Message);
        }
    }
}
