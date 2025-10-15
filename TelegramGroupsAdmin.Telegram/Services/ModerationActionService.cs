using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Services.Telegram;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Centralized moderation action service used by both bot commands and UI.
/// Ensures consistent behavior for spam marking, banning, warning, trusting, and unbanning.
/// </summary>
public class ModerationActionService
{
    private readonly IDetectionResultsRepository _detectionResultsRepository;
    private readonly IUserActionsRepository _userActionsRepository;
    private readonly MessageHistoryRepository _messageHistoryRepository;
    private readonly IManagedChatsRepository _managedChatsRepository;
    private readonly ITelegramUserMappingRepository _telegramUserMappingRepository;
    private readonly TelegramBotClientFactory _botClientFactory;
    private readonly TelegramOptions _telegramOptions;
    private readonly ILogger<ModerationActionService> _logger;

    public ModerationActionService(
        IDetectionResultsRepository detectionResultsRepository,
        IUserActionsRepository userActionsRepository,
        MessageHistoryRepository messageHistoryRepository,
        IManagedChatsRepository managedChatsRepository,
        ITelegramUserMappingRepository telegramUserMappingRepository,
        TelegramBotClientFactory botClientFactory,
        IOptions<TelegramOptions> telegramOptions,
        ILogger<ModerationActionService> logger)
    {
        _detectionResultsRepository = detectionResultsRepository;
        _userActionsRepository = userActionsRepository;
        _messageHistoryRepository = messageHistoryRepository;
        _managedChatsRepository = managedChatsRepository;
        _telegramUserMappingRepository = telegramUserMappingRepository;
        _botClientFactory = botClientFactory;
        _telegramOptions = telegramOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Mark message as spam, delete it, ban user globally, remove trust, and create detection result.
    /// Used by: /spam command, Messages.razor "Mark as Spam", Reports "Spam & Ban" action
    /// </summary>
    public async Task<ModerationResult> MarkAsSpamAndBanAsync(
        ITelegramBotClient botClient,
        long messageId,
        long userId,
        long chatId,
        string? executorId, // Web app user ID or null
        string reason,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = new ModerationResult();

            // 1. Delete the message from Telegram
            try
            {
                await botClient.DeleteMessage(chatId, (int)messageId, cancellationToken);
                result.MessageDeleted = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete message {MessageId} in chat {ChatId} (may already be deleted)", messageId, chatId);
            }

            // 2. Mark message as deleted in database
            await _messageHistoryRepository.MarkMessageAsDeletedAsync(messageId, "spam_action");

            // 3. Create detection result (manual spam classification)
            var detectionResult = new DetectionResultRecord
            {
                MessageId = messageId,
                DetectedAt = DateTimeOffset.UtcNow,
                DetectionSource = "manual",
                DetectionMethod = "Manual",
                IsSpam = true,
                Confidence = 100,
                Reason = reason,
                AddedBy = executorId,
                UserId = userId,
                UsedForTraining = true, // Manual decisions are always training-worthy
                NetConfidence = null,
                CheckResultsJson = null,
                EditVersion = 0
            };
            await _detectionResultsRepository.InsertAsync(detectionResult);

            // 4. Remove any existing trust actions (compromised account protection)
            await _userActionsRepository.ExpireTrustsForUserAsync(userId);
            result.TrustRemoved = true;

            // 5. Ban user globally across all managed chats
            var allChats = await _managedChatsRepository.GetAllChatsAsync();
            foreach (var chat in allChats.Where(c => c.IsActive))
            {
                try
                {
                    await botClient.BanChatMember(
                        chatId: chat.ChatId,
                        userId: userId,
                        cancellationToken: cancellationToken);
                    result.ChatsAffected++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to ban user {UserId} from chat {ChatId}", userId, chat.ChatId);
                }
            }

            // 6. Record ban action
            var banAction = new UserActionRecord(
                Id: 0,
                UserId: userId,
                ActionType: UserActionType.Ban,
                MessageId: messageId,
                IssuedBy: executorId,
                IssuedAt: DateTimeOffset.UtcNow,
                ExpiresAt: null, // Permanent ban
                Reason: reason
            );
            await _userActionsRepository.InsertAsync(banAction);

            _logger.LogInformation(
                "Spam action completed: User {UserId} banned from {ChatsAffected} chats, trust removed, message {MessageId} deleted",
                userId, result.ChatsAffected, messageId);

            result.Success = true;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute spam and ban action for user {UserId}", userId);
            return new ModerationResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// UI-friendly overload: Mark message as spam without requiring bot client parameter
    /// </summary>
    public async Task<ModerationResult> MarkAsSpamAndBanAsync(
        long messageId,
        long userId,
        long chatId,
        string? executorId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var botClient = _botClientFactory.GetOrCreate(_telegramOptions.BotToken);
        return await MarkAsSpamAndBanAsync(botClient, messageId, userId, chatId, executorId, reason, cancellationToken);
    }

    /// <summary>
    /// Ban user globally across all managed chats.
    /// Used by: /ban command, Reports "Ban" action
    /// </summary>
    public async Task<ModerationResult> BanUserAsync(
        ITelegramBotClient botClient,
        long userId,
        long? messageId,
        string? executorId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = new ModerationResult();

            // Remove trust actions
            await _userActionsRepository.ExpireTrustsForUserAsync(userId);
            result.TrustRemoved = true;

            // Ban globally
            var allChats = await _managedChatsRepository.GetAllChatsAsync();
            foreach (var chat in allChats.Where(c => c.IsActive))
            {
                try
                {
                    await botClient.BanChatMember(
                        chatId: chat.ChatId,
                        userId: userId,
                        cancellationToken: cancellationToken);
                    result.ChatsAffected++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to ban user {UserId} from chat {ChatId}", userId, chat.ChatId);
                }
            }

            // Record ban action
            var banAction = new UserActionRecord(
                Id: 0,
                UserId: userId,
                ActionType: UserActionType.Ban,
                MessageId: messageId,
                IssuedBy: executorId,
                IssuedAt: DateTimeOffset.UtcNow,
                ExpiresAt: null,
                Reason: reason
            );
            await _userActionsRepository.InsertAsync(banAction);

            _logger.LogInformation("Ban action completed: User {UserId} banned from {ChatsAffected} chats", userId, result.ChatsAffected);

            result.Success = true;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ban user {UserId}", userId);
            return new ModerationResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// Warn user globally.
    /// Used by: /warn command, Reports "Warn" action
    /// </summary>
    public async Task<ModerationResult> WarnUserAsync(
        long userId,
        long? messageId,
        string? executorId,
        string reason)
    {
        try
        {
            var warnAction = new UserActionRecord(
                Id: 0,
                UserId: userId,
                ActionType: UserActionType.Warn,
                MessageId: messageId,
                IssuedBy: executorId,
                IssuedAt: DateTimeOffset.UtcNow,
                ExpiresAt: null,
                Reason: reason
            );
            await _userActionsRepository.InsertAsync(warnAction);

            _logger.LogInformation("Warn action completed: User {UserId} warned", userId);

            return new ModerationResult { Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to warn user {UserId}", userId);
            return new ModerationResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// Trust user globally (bypass spam detection).
    /// Used by: /trust command, UI trust button
    /// </summary>
    public async Task<ModerationResult> TrustUserAsync(
        long userId,
        string? executorId,
        string reason)
    {
        try
        {
            var trustAction = new UserActionRecord(
                Id: 0,
                UserId: userId,
                ActionType: UserActionType.Trust,
                MessageId: null,
                IssuedBy: executorId,
                IssuedAt: DateTimeOffset.UtcNow,
                ExpiresAt: null,
                Reason: reason
            );
            await _userActionsRepository.InsertAsync(trustAction);

            _logger.LogInformation("Trust action completed: User {UserId} trusted globally", userId);

            return new ModerationResult { Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to trust user {UserId}", userId);
            return new ModerationResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// Unban user globally and optionally restore trust.
    /// Used by: /unban command, Messages.razor "Mark as Ham", Reports "Dismiss" action
    /// </summary>
    public async Task<ModerationResult> UnbanUserAsync(
        ITelegramBotClient botClient,
        long userId,
        string? executorId,
        string reason,
        bool restoreTrust = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = new ModerationResult();

            // Expire all active bans
            await _userActionsRepository.ExpireBansForUserAsync(userId);

            // Unban from all chats
            var allChats = await _managedChatsRepository.GetAllChatsAsync();
            foreach (var chat in allChats.Where(c => c.IsActive))
            {
                try
                {
                    await botClient.UnbanChatMember(
                        chatId: chat.ChatId,
                        userId: userId,
                        onlyIfBanned: true,
                        cancellationToken: cancellationToken);
                    result.ChatsAffected++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to unban user {UserId} from chat {ChatId}", userId, chat.ChatId);
                }
            }

            // Record unban action
            var unbanAction = new UserActionRecord(
                Id: 0,
                UserId: userId,
                ActionType: UserActionType.Unban,
                MessageId: null,
                IssuedBy: executorId,
                IssuedAt: DateTimeOffset.UtcNow,
                ExpiresAt: null,
                Reason: reason
            );
            await _userActionsRepository.InsertAsync(unbanAction);

            // Optionally restore trust (for false positive corrections)
            if (restoreTrust)
            {
                await TrustUserAsync(userId, executorId, "Trust restored after unban (false positive correction)");
                result.TrustRestored = true;
            }

            _logger.LogInformation(
                "Unban action completed: User {UserId} unbanned from {ChatsAffected} chats, trust restored: {TrustRestored}",
                userId, result.ChatsAffected, restoreTrust);

            result.Success = true;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unban user {UserId}", userId);
            return new ModerationResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// UI-friendly overload: Unban user without requiring bot client parameter
    /// </summary>
    public async Task<ModerationResult> UnbanUserAsync(
        long userId,
        string? executorId,
        string reason,
        bool restoreTrust = false,
        CancellationToken cancellationToken = default)
    {
        var botClient = _botClientFactory.GetOrCreate(_telegramOptions.BotToken);
        return await UnbanUserAsync(botClient, userId, executorId, reason, restoreTrust, cancellationToken);
    }

    /// <summary>
    /// Delete a message from Telegram and mark as deleted in database
    /// Used by: Messages.razor "Delete" button
    /// </summary>
    public async Task<ModerationResult> DeleteMessageAsync(
        long messageId,
        long chatId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = new ModerationResult();

            // Get bot client from factory (singleton instance)
            var botClient = _botClientFactory.GetOrCreate(_telegramOptions.BotToken);

            // 1. Delete the message from Telegram
            try
            {
                await botClient.DeleteMessage(chatId, (int)messageId, cancellationToken);
                result.MessageDeleted = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete message {MessageId} in chat {ChatId} (may already be deleted)", messageId, chatId);
            }

            // 2. Mark message as deleted in database
            await _messageHistoryRepository.MarkMessageAsDeletedAsync(messageId, "manual_ui_delete");

            _logger.LogInformation("Deleted message {MessageId} from chat {ChatId} via UI", messageId, chatId);

            result.Success = true;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete message {MessageId}", messageId);
            return new ModerationResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// Map Telegram user ID to web app user ID (for audit trail)
    /// </summary>
    public async Task<string?> GetExecutorUserIdAsync(long? telegramUserId)
    {
        if (telegramUserId == null) return null;
        return await _telegramUserMappingRepository.GetUserIdByTelegramIdAsync(telegramUserId.Value);
    }
}

/// <summary>
/// Result of a moderation action
/// </summary>
public class ModerationResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public bool MessageDeleted { get; set; }
    public bool TrustRemoved { get; set; }
    public bool TrustRestored { get; set; }
    public int ChatsAffected { get; set; }
}
