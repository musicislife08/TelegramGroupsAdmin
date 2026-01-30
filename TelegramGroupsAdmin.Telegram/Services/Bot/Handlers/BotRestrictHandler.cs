using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Results;
using TelegramGroupsAdmin.Telegram.Constants;

namespace TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;

/// <summary>
/// Low-level handler for restriction/mute operations.
/// Used by welcome flow and future restriction features.
/// Supports both single-chat (chatId > 0) and global (chatId = 0) restrictions.
/// This is the ONLY layer that should touch ITelegramBotClientFactory for restrict operations.
/// </summary>
public class BotRestrictHandler : IBotRestrictHandler
{
    private readonly IBotChatService _chatService;
    private readonly ITelegramBotClientFactory _botClientFactory;
    private readonly ITelegramUserRepository _userRepository;
    private readonly IManagedChatsRepository _chatsRepository;
    private readonly ILogger<BotRestrictHandler> _logger;

    public BotRestrictHandler(
        IBotChatService chatService,
        ITelegramBotClientFactory botClientFactory,
        ITelegramUserRepository userRepository,
        IManagedChatsRepository chatsRepository,
        ILogger<BotRestrictHandler> logger)
    {
        _chatService = chatService;
        _botClientFactory = botClientFactory;
        _userRepository = userRepository;
        _chatsRepository = chatsRepository;
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
        // Fetch once for logging
        var user = await _userRepository.GetByTelegramIdAsync(userId, cancellationToken);
        var chat = chatId != ModerationConstants.GlobalChatId
            ? await _chatsRepository.GetByChatIdAsync(chatId, cancellationToken)
            : null;

        var isGlobal = chatId == ModerationConstants.GlobalChatId;
        _logger.LogDebug(
            "Executing {Scope} restriction for user {User} for {Duration} by {Executor}",
            isGlobal ? "global" : $"chat-specific ({chat.ToLogDebug(chatId)})",
            user.ToLogDebug(userId), duration, executor.GetDisplayText());

        try
        {
            var expiresAt = DateTimeOffset.UtcNow.Add(duration);
            var mutePermissions = CreateMutePermissions();

            if (isGlobal)
            {
                return await ExecuteGlobalRestrictionAsync(user, userId, mutePermissions, expiresAt, cancellationToken);
            }
            else
            {
                return await ExecuteSingleChatRestrictionAsync(user, userId, chat, chatId, mutePermissions, expiresAt, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute restriction for user {User}", user.ToLogDebug(userId));
            return RestrictResult.Failed(ex.Message);
        }
    }

    /// <summary>
    /// Execute restriction across all managed chats (global mode).
    /// </summary>
    private async Task<RestrictResult> ExecuteGlobalRestrictionAsync(
        TelegramUser? user,
        long userId,
        ChatPermissions permissions,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        var client = await _botClientFactory.GetBotClientAsync();
        var healthyChatIds = _chatService.GetHealthyChatIds();

        var successCount = 0;
        var failCount = 0;

        await Parallel.ForEachAsync(healthyChatIds, cancellationToken, async (targetChatId, ct) =>
        {
            try
            {
                await client.RestrictChatMember(
                    chatId: targetChatId,
                    userId: userId,
                    permissions: permissions,
                    untilDate: expiresAt.UtcDateTime,
                    cancellationToken: ct);
                Interlocked.Increment(ref successCount);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to restrict user {UserId} in chat {ChatId}", userId, targetChatId);
                Interlocked.Increment(ref failCount);
            }
        });

        _logger.LogInformation(
            "Global restriction completed for {User}: {Success} succeeded, {Failed} failed. Expires at {ExpiresAt}",
            user.ToLogInfo(userId), successCount, failCount, expiresAt);

        return RestrictResult.Succeeded(successCount, expiresAt, failCount);
    }

    /// <summary>
    /// Execute restriction in a single specific chat.
    /// </summary>
    private async Task<RestrictResult> ExecuteSingleChatRestrictionAsync(
        TelegramUser? user,
        long userId,
        ManagedChatRecord? chat,
        long chatId,
        ChatPermissions permissions,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        var client = await _botClientFactory.GetBotClientAsync();

        await client.RestrictChatMember(
            chatId: chatId,
            userId: userId,
            permissions: permissions,
            untilDate: expiresAt.UtcDateTime,
            cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Single-chat restriction completed for {User} in {Chat}. Expires at {ExpiresAt}",
            user.ToLogInfo(userId), chat.ToLogInfo(chatId), expiresAt);

        return RestrictResult.Succeeded(chatsAffected: ModerationConstants.SingleChatSuccess, expiresAt, chatsFailed: ModerationConstants.NoFailures);
    }

    /// <inheritdoc />
    public async Task<RestrictResult> RestorePermissionsAsync(
        long userId,
        long chatId,
        Actor executor,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        // Fetch once for logging
        var user = await _userRepository.GetByTelegramIdAsync(userId, cancellationToken);
        var chat = await _chatsRepository.GetByChatIdAsync(chatId, cancellationToken);

        _logger.LogDebug(
            "Restoring permissions for user {User} in {Chat} by {Executor}",
            user.ToLogDebug(userId), chat.ToLogDebug(chatId), executor.GetDisplayText());

        try
        {
            var client = await _botClientFactory.GetBotClientAsync();

            // Get the chat's default permissions
            var chatDetails = await client.GetChat(chatId, cancellationToken);
            var defaultPermissions = chatDetails.Permissions ?? CreateDefaultPermissions();

            _logger.LogDebug(
                "Restoring {User} to {Chat} default permissions: Messages={CanSendMessages}, Media={CanSendPhotos}",
                user.ToLogDebug(userId), chat.ToLogDebug(chatId),
                defaultPermissions.CanSendMessages, defaultPermissions.CanSendPhotos);

            await client.RestrictChatMember(
                chatId: chatId,
                userId: userId,
                permissions: defaultPermissions,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Restored permissions for {User} in {Chat}",
                user.ToLogInfo(userId), chat.ToLogInfo(chatId));

            return RestrictResult.Succeeded(
                chatsAffected: ModerationConstants.SingleChatSuccess,
                expiresAt: null,
                chatsFailed: ModerationConstants.NoFailures);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore permissions for user {User} in {Chat}",
                user.ToLogDebug(userId), chat.ToLogDebug(chatId));
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
