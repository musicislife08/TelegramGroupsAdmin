using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Results;
using TelegramGroupsAdmin.Telegram.Constants;

namespace TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;

/// <summary>
/// Low-level handler for restriction/mute operations.
/// Used by welcome flow and future restriction features.
/// Supports both single-chat (chat provided) and global (chat null) restrictions.
/// This is the ONLY layer that should touch ITelegramBotClientFactory for restrict operations.
/// </summary>
public class BotRestrictHandler : IBotRestrictHandler
{
    private readonly IBotChatService _chatService;
    private readonly ITelegramBotClientFactory _botClientFactory;
    private readonly ILogger<BotRestrictHandler> _logger;

    public BotRestrictHandler(
        IBotChatService chatService,
        ITelegramBotClientFactory botClientFactory,
        ILogger<BotRestrictHandler> logger)
    {
        _chatService = chatService;
        _botClientFactory = botClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<RestrictResult> RestrictAsync(
        UserIdentity user,
        ChatIdentity? chat,
        Actor executor,
        TimeSpan duration,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var isGlobal = chat is null;
        _logger.LogDebug(
            "Executing {Scope} restriction for user {User} for {Duration} by {Executor}",
            isGlobal ? "global" : $"chat-specific ({chat!.ToLogDebug()})",
            user.ToLogDebug(), duration, executor.GetDisplayText());

        try
        {
            var expiresAt = DateTimeOffset.UtcNow.Add(duration);
            var mutePermissions = CreateMutePermissions();

            if (isGlobal)
            {
                return await ExecuteGlobalRestrictionAsync(user, mutePermissions, expiresAt, cancellationToken);
            }
            else
            {
                return await ExecuteSingleChatRestrictionAsync(user, chat!, mutePermissions, expiresAt, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute restriction for user {User}", user.ToLogDebug());
            return RestrictResult.Failed(ex.Message);
        }
    }

    /// <summary>
    /// Execute restriction across all managed chats (global mode).
    /// </summary>
    private async Task<RestrictResult> ExecuteGlobalRestrictionAsync(
        UserIdentity user,
        ChatPermissions permissions,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        var apiClient = await _botClientFactory.GetApiClientAsync();
        var healthyChatIds = _chatService.GetHealthyChatIds();

        var successCount = 0;
        var failCount = 0;

        await Parallel.ForEachAsync(healthyChatIds, cancellationToken, async (targetChatId, ct) =>
        {
            try
            {
                await apiClient.RestrictChatMemberAsync(
                    chatId: targetChatId,
                    userId: user.Id,
                    permissions: permissions,
                    untilDate: expiresAt.UtcDateTime,
                    ct: ct);
                Interlocked.Increment(ref successCount);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to restrict user {UserId} in chat {ChatId}", user.Id, targetChatId);
                Interlocked.Increment(ref failCount);
            }
        });

        _logger.LogInformation(
            "Global restriction completed for {User}: {Success} succeeded, {Failed} failed. Expires at {ExpiresAt}",
            user.ToLogInfo(), successCount, failCount, expiresAt);

        return RestrictResult.Succeeded(successCount, expiresAt, failCount);
    }

    /// <summary>
    /// Execute restriction in a single specific chat.
    /// </summary>
    private async Task<RestrictResult> ExecuteSingleChatRestrictionAsync(
        UserIdentity user,
        ChatIdentity chat,
        ChatPermissions permissions,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        var apiClient = await _botClientFactory.GetApiClientAsync();

        await apiClient.RestrictChatMemberAsync(
            chatId: chat.Id,
            userId: user.Id,
            permissions: permissions,
            untilDate: expiresAt.UtcDateTime,
            ct: cancellationToken);

        _logger.LogInformation(
            "Single-chat restriction completed for {User} in {Chat}. Expires at {ExpiresAt}",
            user.ToLogInfo(), chat.ToLogInfo(), expiresAt);

        return RestrictResult.Succeeded(chatsAffected: ModerationConstants.SingleChatSuccess, expiresAt, chatsFailed: ModerationConstants.NoFailures);
    }

    /// <inheritdoc />
    public async Task<RestrictResult> RestorePermissionsAsync(
        UserIdentity user,
        ChatIdentity chat,
        Actor executor,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Restoring permissions for user {User} in {Chat} by {Executor}",
            user.ToLogDebug(), chat.ToLogDebug(), executor.GetDisplayText());

        try
        {
            var apiClient = await _botClientFactory.GetApiClientAsync();

            // Get the chat's default permissions
            var chatDetails = await apiClient.GetChatAsync(chat.Id, cancellationToken);
            var defaultPermissions = chatDetails.Permissions ?? CreateDefaultPermissions();

            _logger.LogDebug(
                "Restoring {User} to {Chat} default permissions: Messages={CanSendMessages}, Media={CanSendPhotos}",
                user.ToLogDebug(), chat.ToLogDebug(),
                defaultPermissions.CanSendMessages, defaultPermissions.CanSendPhotos);

            await apiClient.RestrictChatMemberAsync(
                chatId: chat.Id,
                userId: user.Id,
                permissions: defaultPermissions,
                ct: cancellationToken);

            _logger.LogInformation(
                "Restored permissions for {User} in {Chat}",
                user.ToLogInfo(), chat.ToLogInfo());

            return RestrictResult.Succeeded(
                chatsAffected: ModerationConstants.SingleChatSuccess,
                expiresAt: null,
                chatsFailed: ModerationConstants.NoFailures);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore permissions for user {User} in {Chat}",
                user.ToLogDebug(), chat.ToLogDebug());
            return RestrictResult.Failed(ex.Message);
        }
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

    /// <summary>
    /// Create default permissions (standard member permissions).
    /// Used as fallback when chat doesn't have explicit default permissions.
    /// </summary>
    private static ChatPermissions CreateDefaultPermissions() => new()
    {
        CanSendMessages = true,
        CanSendAudios = true,
        CanSendDocuments = true,
        CanSendPhotos = true,
        CanSendVideos = true,
        CanSendVideoNotes = true,
        CanSendVoiceNotes = true,
        CanSendPolls = true,
        CanSendOtherMessages = true,
        CanAddWebPagePreviews = true,
        CanChangeInfo = false,
        CanInviteUsers = true,
        CanPinMessages = false,
        CanManageTopics = false
    };
}
