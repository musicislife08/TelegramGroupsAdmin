using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Results;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Infrastructure;
using TelegramGroupsAdmin.Telegram.Constants;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions;

/// <summary>
/// Domain handler for restriction/mute operations.
/// Used by welcome flow and future restriction features.
/// Supports both single-chat (chatId > 0) and global (chatId = 0) restrictions.
/// </summary>
public class RestrictHandler : IRestrictHandler
{
    private readonly ITelegramBotClientFactory _botClientFactory;
    private readonly ICrossChatExecutor _crossChatExecutor;
    private readonly ILogger<RestrictHandler> _logger;

    public RestrictHandler(
        ITelegramBotClientFactory botClientFactory,
        ICrossChatExecutor crossChatExecutor,
        ILogger<RestrictHandler> logger)
    {
        _botClientFactory = botClientFactory;
        _crossChatExecutor = crossChatExecutor;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<RestrictResult> RestrictAsync(
        long userId,
        long chatId,
        Actor executor,
        TimeSpan duration,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var isGlobal = chatId == ModerationConstants.GlobalChatId;
        _logger.LogDebug(
            "Executing {Scope} restriction for user {UserId} for {Duration} by {Executor}",
            isGlobal ? "global" : $"chat-specific (chat {chatId})",
            userId, duration, executor.GetDisplayText());

        try
        {
            var expiresAt = DateTimeOffset.UtcNow.Add(duration);
            var mutePermissions = CreateMutePermissions();

            if (isGlobal)
            {
                return await ExecuteGlobalRestrictionAsync(userId, mutePermissions, expiresAt, cancellationToken);
            }
            else
            {
                return await ExecuteSingleChatRestrictionAsync(userId, chatId, mutePermissions, expiresAt, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute restriction for user {UserId}", userId);
            return RestrictResult.Failed(ex.Message);
        }
    }

    /// <summary>
    /// Execute restriction across all managed chats (global mode).
    /// </summary>
    private async Task<RestrictResult> ExecuteGlobalRestrictionAsync(
        long userId,
        ChatPermissions permissions,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        var crossResult = await _crossChatExecutor.ExecuteAcrossChatsAsync(
            async (ops, targetChatId, token) => await ops.RestrictChatMemberAsync(
                chatId: targetChatId,
                userId: userId,
                permissions: permissions,
                untilDate: expiresAt.UtcDateTime,
                cancellationToken: token),
            "Restrict",
            cancellationToken);

        _logger.LogInformation(
            "Global restriction completed for user {UserId}: {Success} succeeded, {Failed} failed. Expires at {ExpiresAt}",
            userId, crossResult.SuccessCount, crossResult.FailCount, expiresAt);

        return RestrictResult.Succeeded(crossResult.SuccessCount, expiresAt, crossResult.FailCount);
    }

    /// <summary>
    /// Execute restriction in a single specific chat.
    /// </summary>
    private async Task<RestrictResult> ExecuteSingleChatRestrictionAsync(
        long userId,
        long chatId,
        ChatPermissions permissions,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        var operations = await _botClientFactory.GetOperationsAsync();

        await operations.RestrictChatMemberAsync(
            chatId: chatId,
            userId: userId,
            permissions: permissions,
            untilDate: expiresAt.UtcDateTime,
            cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Single-chat restriction completed for user {UserId} in chat {ChatId}. Expires at {ExpiresAt}",
            userId, chatId, expiresAt);

        return RestrictResult.Succeeded(chatsAffected: ModerationConstants.SingleChatSuccess, expiresAt, chatsFailed: ModerationConstants.NoFailures);
    }

    /// <summary>
    /// Create full mute permissions (all permissions disabled).
    /// </summary>
    private static ChatPermissions CreateMutePermissions() => new()
    {
        CanSendMessages = false,
        CanSendAudios = false,
        CanSendDocuments = false,
        CanSendPhotos = false,
        CanSendVideos = false,
        CanSendVideoNotes = false,
        CanSendVoiceNotes = false,
        CanSendPolls = false,
        CanSendOtherMessages = false,
        CanAddWebPagePreviews = false,
        CanChangeInfo = false,
        CanInviteUsers = false,
        CanPinMessages = false,
        CanManageTopics = false
    };
}
