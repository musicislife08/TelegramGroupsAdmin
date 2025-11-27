using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Core;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Abstractions.Services;
using TelegramGroupsAdmin.Telegram.Abstractions.Jobs;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Services.Notifications;
using TelegramGroupsAdmin.Telegram.Services.BackgroundServices;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Centralized moderation action service used by both bot commands and UI.
/// Ensures consistent behavior for spam marking, banning, warning, trusting, and unbanning.
/// </summary>
public class ModerationActionService
{
    private readonly IDetectionResultsRepository _detectionResultsRepository;
    private readonly IUserActionsRepository _userActionsRepository;
    private readonly IMessageHistoryRepository _messageHistoryRepository;
    private readonly IManagedChatsRepository _managedChatsRepository;
    private readonly ITelegramUserMappingRepository _telegramUserMappingRepository;
    private readonly IImageTrainingSamplesRepository _imageTrainingSamplesRepository;
    private readonly TelegramBotClientFactory _botClientFactory;
    private readonly TelegramConfigLoader _configLoader;
    private readonly IConfigService _configService;
    private readonly IServiceProvider _serviceProvider;
    private readonly IUserMessagingService _messagingService;
    private readonly IChatInviteLinkService _inviteLinkService;
    private readonly INotificationOrchestrator _notificationOrchestrator;
    private readonly BotMessageService _botMessageService;
    private readonly ChatManagementService _chatManagementService;
    private readonly IJobScheduler _jobScheduler;
    private readonly ILogger<ModerationActionService> _logger;

    public ModerationActionService(
        IDetectionResultsRepository detectionResultsRepository,
        IUserActionsRepository userActionsRepository,
        IMessageHistoryRepository messageHistoryRepository,
        IManagedChatsRepository managedChatsRepository,
        ITelegramUserMappingRepository telegramUserMappingRepository,
        IImageTrainingSamplesRepository imageTrainingSamplesRepository,
        TelegramBotClientFactory botClientFactory,
        TelegramConfigLoader configLoader,
        IConfigService configService,
        IServiceProvider serviceProvider,
        IUserMessagingService messagingService,
        IChatInviteLinkService inviteLinkService,
        INotificationOrchestrator notificationOrchestrator,
        BotMessageService botMessageService,
        ChatManagementService chatManagementService,
        IJobScheduler jobScheduler,
        ILogger<ModerationActionService> logger)
    {
        _detectionResultsRepository = detectionResultsRepository;
        _userActionsRepository = userActionsRepository;
        _messageHistoryRepository = messageHistoryRepository;
        _managedChatsRepository = managedChatsRepository;
        _telegramUserMappingRepository = telegramUserMappingRepository;
        _imageTrainingSamplesRepository = imageTrainingSamplesRepository;
        _botClientFactory = botClientFactory;
        _configLoader = configLoader;
        _configService = configService;
        _serviceProvider = serviceProvider;
        _messagingService = messagingService;
        _inviteLinkService = inviteLinkService;
        _notificationOrchestrator = notificationOrchestrator;
        _botMessageService = botMessageService;
        _chatManagementService = chatManagementService;
        _jobScheduler = jobScheduler;
        _logger = logger;
    }

    /// <summary>
    /// Checks if user is Telegram's service account (777000) and returns error if moderation is attempted.
    /// Service account is used for channel posts and anonymous admin posts and must be exempt from moderation.
    /// </summary>
    private ModerationResult? CheckServiceAccountProtection(long userId)
    {
        if (userId == TelegramConstants.ServiceAccountUserId)
        {
            _logger.LogWarning(
                "Moderation action blocked for Telegram service account (user {UserId}). " +
                "This user represents channel posts and anonymous admin posts and cannot be moderated.",
                userId);

            return new ModerationResult
            {
                Success = false,
                ErrorMessage = "Cannot perform moderation actions on Telegram service account (channel/anonymous posts)"
            };
        }

        return null; // No protection needed, proceed with moderation
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
        Actor executor,
        string reason,
        global::Telegram.Bot.Types.Message? telegramMessage = null,
        CancellationToken cancellationToken = default)
    {
        // Protect Telegram service account from moderation
        var protectionResult = CheckServiceAccountProtection(userId);
        if (protectionResult != null)
            return protectionResult;

        try
        {
            var result = new ModerationResult();

            // 1. Delete the message from Telegram and mark as deleted in database
            try
            {
                await _botMessageService.DeleteAndMarkMessageAsync(
                    botClient,
                    chatId,
                    (int)messageId,
                    deletionSource: "spam_action",
                    cancellationToken);
                result.MessageDeleted = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete message {MessageId} in chat {ChatId} (may already be deleted)", messageId, chatId);
            }

            // 3. Check if message exists in database (may not if bot wasn't running when message was sent)
            var message = await _messageHistoryRepository.GetMessageAsync(messageId, cancellationToken);

            if (message != null)
            {
                // 3.5. CRITICAL: Invalidate old training data to prevent cross-class conflicts
                await _detectionResultsRepository.InvalidateTrainingDataForMessageAsync(
                    messageId,
                    cancellationToken);

                // 4. Create detection result (manual spam classification) - message exists in DB
                var hasText = !string.IsNullOrWhiteSpace(message.MessageText);
                var detectionResult = new DetectionResultRecord
                {
                    MessageId = messageId,
                    DetectedAt = DateTimeOffset.UtcNow,
                    DetectionSource = "manual",
                    DetectionMethod = "Manual",
                    // IsSpam computed from net_confidence (100 = strong spam)
                    Confidence = 100, // 100% spam
                    Reason = reason,
                    AddedBy = executor,
                    UserId = userId,
                    UsedForTraining = hasText, // Only use text messages for training (image-only spam excluded)
                    NetConfidence = 100, // Strong spam signal (manual admin decision)
                    CheckResultsJson = null,
                    EditVersion = 0
                };
                await _detectionResultsRepository.InsertAsync(detectionResult, cancellationToken);

                // 4b. ML-5: Save image training sample if message has a photo
                await _imageTrainingSamplesRepository.SaveTrainingSampleAsync(
                    messageId,
                    isSpam: true,
                    executor,
                    cancellationToken);
            }
            else if (telegramMessage != null)
            {
                // Message not in database (bot wasn't running when message was sent)
                // Backfill from the Telegram Message object provided by caller
                try
                {
                    var messageText = telegramMessage.Text ?? telegramMessage.Caption;

                    if (!string.IsNullOrWhiteSpace(messageText))
                    {
                        // Backfill the missing message record with actual Telegram data
                        var messageRecord = new MessageRecord(
                            MessageId: messageId,
                            UserId: userId,
                            UserName: telegramMessage.From?.Username,
                            FirstName: telegramMessage.From?.FirstName,
                            ChatId: chatId,
                            Timestamp: telegramMessage.Date,
                            MessageText: messageText,
                            PhotoFileId: null,
                            PhotoFileSize: null,
                            Urls: null,
                            EditDate: null,
                            ContentHash: null,
                            ChatName: null,
                            PhotoLocalPath: null,
                            PhotoThumbnailPath: null,
                            ChatIconPath: null,
                            UserPhotoPath: null,
                            DeletedAt: null,
                            DeletionSource: null,
                            ReplyToMessageId: null,
                            ReplyToUser: null,
                            ReplyToText: null,
                            MediaType: null,
                            MediaFileId: null,
                            MediaFileSize: null,
                            MediaFileName: null,
                            MediaMimeType: null,
                            MediaLocalPath: null,
                            MediaDuration: null,
                            Translation: null,
                            ContentCheckSkipReason: ContentCheckSkipReason.NotSkipped
                        );

                        await _messageHistoryRepository.InsertMessageAsync(messageRecord, cancellationToken);

                        // Invalidate any existing training data (shouldn't exist for backfill, but be safe)
                        await _detectionResultsRepository.InvalidateTrainingDataForMessageAsync(
                            messageId,
                            cancellationToken);

                        // Now create the detection result with the real message_id
                        var detectionResult = new DetectionResultRecord
                        {
                            MessageId = messageId,
                            DetectedAt = DateTimeOffset.UtcNow,
                            DetectionSource = "manual",
                            DetectionMethod = "Manual",
                            Confidence = 100,
                            Reason = reason,
                            AddedBy = executor,
                            UserId = userId,
                            UsedForTraining = true, // Text spam marked via /spam should be used for training
                            NetConfidence = 100,
                            CheckResultsJson = null,
                            EditVersion = 0
                        };
                        await _detectionResultsRepository.InsertAsync(detectionResult, cancellationToken);

                        _logger.LogInformation(
                            "Message {MessageId} not in database. Backfilled from Telegram with real data for user {UserId}",
                            messageId, userId);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Message {MessageId} has no text content. Skipping backfill for user {UserId}",
                            messageId, userId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to backfill message {MessageId}. Skipping training data, but proceeding with ban for user {UserId}",
                        messageId, userId);
                }
            }
            else
            {
                // No message data available (called from UI without Telegram Message object)
                _logger.LogWarning(
                    "Message {MessageId} not in database and no Telegram message provided. Skipping training data for user {UserId}",
                    messageId, userId);
            }

            // 5. Remove any existing trust actions (compromised account protection)
            await _userActionsRepository.ExpireTrustsForUserAsync(userId, chatId: null, cancellationToken);
            result.TrustRemoved = true;

            // 6. Ban user globally across all managed chats (PERF-TG-2: parallel execution with concurrency limit)
            var allChats = await _managedChatsRepository.GetAllChatsAsync(cancellationToken);
            var activeChatIds = allChats.Where(c => c.IsActive).Select(c => c.ChatId).ToList();

            // Health gate: Filter for chats where bot has confirmed permissions
            var actionableChatIds = _chatManagementService.FilterHealthyChats(activeChatIds);

            // Log chats skipped due to health issues
            var skippedChats = activeChatIds.Except(actionableChatIds).ToList();
            if (skippedChats.Count > 0)
            {
                _logger.LogWarning(
                    "Skipping {Count} unhealthy chats for ban action: {ChatIds}. " +
                    "Bot lacks required permissions (admin + ban members) in these chats.",
                    skippedChats.Count,
                    string.Join(", ", skippedChats));
            }

            // Parallel execution with concurrency limit (respects Telegram rate limits)
            using var semaphore = new SemaphoreSlim(3); // Max 3 concurrent API calls
            var banTasks = actionableChatIds.Select(async chatId =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    await botClient.BanChatMember(
                        chatId: chatId,
                        userId: userId,
                        cancellationToken: cancellationToken);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to ban user {UserId} from chat {ChatId}", userId, chatId);
                    return false;
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var banResults = await Task.WhenAll(banTasks);
            result.ChatsAffected = banResults.Count(success => success);

            // 6. Record ban action
            var banAction = new UserActionRecord(
                Id: 0,
                UserId: userId,
                ActionType: UserActionType.Ban,
                MessageId: messageId,
                IssuedBy: executor,
                IssuedAt: DateTimeOffset.UtcNow,
                ExpiresAt: null, // Permanent ban
                Reason: reason
            );
            await _userActionsRepository.InsertAsync(banAction, cancellationToken);

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
        Actor executor,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var botToken = await _configLoader.LoadConfigAsync();
        var botClient = _botClientFactory.GetOrCreate(botToken);
        return await MarkAsSpamAndBanAsync(botClient, messageId, userId, chatId, executor, reason, null, cancellationToken);
    }

    /// <summary>
    /// Ban user globally across all managed chats.
    /// Used by: /ban command, Reports "Ban" action
    /// </summary>
    public async Task<ModerationResult> BanUserAsync(
        ITelegramBotClient botClient,
        long userId,
        long? messageId,
        Actor executor,
        string reason,
        CancellationToken cancellationToken = default)
    {
        // Protect Telegram service account from moderation
        var protectionResult = CheckServiceAccountProtection(userId);
        if (protectionResult != null)
            return protectionResult;

        try
        {
            var result = new ModerationResult();

            // Remove trust actions
            await _userActionsRepository.ExpireTrustsForUserAsync(userId, chatId: null, cancellationToken);
            result.TrustRemoved = true;

            // Ban globally (PERF-TG-2: parallel execution with concurrency limit)
            var allChats = await _managedChatsRepository.GetAllChatsAsync(cancellationToken);
            var activeChatIds = allChats.Where(c => c.IsActive).Select(c => c.ChatId).ToList();

            // Health gate: Filter for chats where bot has confirmed permissions
            var actionableChatIds = _chatManagementService.FilterHealthyChats(activeChatIds);

            // Log chats skipped due to health issues
            var skippedChats = activeChatIds.Except(actionableChatIds).ToList();
            if (skippedChats.Count > 0)
            {
                _logger.LogWarning(
                    "Skipping {Count} unhealthy chats for ban action: {ChatIds}. " +
                    "Bot lacks required permissions (admin + ban members) in these chats.",
                    skippedChats.Count,
                    string.Join(", ", skippedChats));
            }

            using var semaphore = new SemaphoreSlim(3);
            var banTasks = actionableChatIds.Select(async chatId =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    await botClient.BanChatMember(
                        chatId: chatId,
                        userId: userId,
                        cancellationToken: cancellationToken);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to ban user {UserId} from chat {ChatId}", userId, chatId);
                    return false;
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var banResults = await Task.WhenAll(banTasks);
            result.ChatsAffected = banResults.Count(success => success);

            // Record ban action
            var banAction = new UserActionRecord(
                Id: 0,
                UserId: userId,
                ActionType: UserActionType.Ban,
                MessageId: messageId,
                IssuedBy: executor,
                IssuedAt: DateTimeOffset.UtcNow,
                ExpiresAt: null,
                Reason: reason
            );
            await _userActionsRepository.InsertAsync(banAction, cancellationToken);

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
    /// Warn user globally with automatic ban after threshold.
    /// Used by: /warn command, Reports "Warn" action
    /// </summary>
    public async Task<ModerationResult> WarnUserAsync(
        long userId,
        long? messageId,
        Actor executor,
        string reason,
        long? chatId = null,
        CancellationToken cancellationToken = default)
    {
        // Protect Telegram service account from moderation
        var protectionResult = CheckServiceAccountProtection(userId);
        if (protectionResult != null)
            return protectionResult;

        try
        {
            // 1. Insert the warning
            var warnAction = new UserActionRecord(
                Id: 0,
                UserId: userId,
                ActionType: UserActionType.Warn,
                MessageId: messageId,
                IssuedBy: executor,
                IssuedAt: DateTimeOffset.UtcNow,
                ExpiresAt: null,
                Reason: reason
            );
            await _userActionsRepository.InsertAsync(warnAction, cancellationToken);

            // 2. Get current warning count
            var warnCount = await _userActionsRepository.GetWarnCountAsync(userId, chatId, cancellationToken);

            _logger.LogInformation("Warn action completed: User {UserId} warned (total warnings: {WarnCount})", userId, warnCount);

            var result = new ModerationResult
            {
                Success = true,
                WarningCount = warnCount
            };

            // 2.5. Send DM notification to user about the warning
            await SendWarningNotificationAsync(userId, warnCount, reason, cancellationToken);

            // 3. Check if auto-ban threshold is reached
            var warningConfig = await _configService.GetEffectiveAsync<WarningSystemConfig>(ConfigType.Moderation, chatId)
                               ?? WarningSystemConfig.Default;

            if (warningConfig.AutoBanEnabled &&
                warningConfig.AutoBanThreshold > 0 &&
                warnCount >= warningConfig.AutoBanThreshold)
            {
                // 4. Auto-ban user
                var autoBanReason = warningConfig.AutoBanReason.Replace("{count}", warnCount.ToString());
                var botToken = await _configLoader.LoadConfigAsync();
                var botClient = _botClientFactory.GetOrCreate(botToken);

                var autoBanExecutor = Actor.AutoBan;
                var banResult = await BanUserAsync(
                    botClient: botClient,
                    userId: userId,
                    messageId: messageId,
                    executor: autoBanExecutor,
                    reason: autoBanReason,
                    cancellationToken: cancellationToken);

                if (banResult.Success)
                {
                    result.AutoBanTriggered = true;
                    result.ChatsAffected = banResult.ChatsAffected;
                    _logger.LogWarning(
                        "Auto-ban triggered: User {UserId} banned after {WarnCount} warnings (threshold: {Threshold})",
                        userId, warnCount, warningConfig.AutoBanThreshold);
                }
                else
                {
                    _logger.LogError("Auto-ban failed for user {UserId}: {Error}", userId, banResult.ErrorMessage);
                }
            }

            return result;
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
        Actor executor,
        string reason,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var trustAction = new UserActionRecord(
                Id: 0,
                UserId: userId,
                ActionType: UserActionType.Trust,
                MessageId: null,
                IssuedBy: executor,
                IssuedAt: DateTimeOffset.UtcNow,
                ExpiresAt: null,
                Reason: reason
            );
            await _userActionsRepository.InsertAsync(trustAction, cancellationToken);

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
        Actor executor,
        string reason,
        bool restoreTrust = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = new ModerationResult();

            // Expire all active bans
            await _userActionsRepository.ExpireBansForUserAsync(userId, chatId: null, cancellationToken);

            // Unban from all chats (PERF-TG-2: parallel execution with concurrency limit)
            var allChats = await _managedChatsRepository.GetAllChatsAsync(cancellationToken);
            var activeChatIds = allChats.Where(c => c.IsActive).Select(c => c.ChatId).ToList();

            // Health gate: Filter for chats where bot has confirmed permissions
            var actionableChatIds = _chatManagementService.FilterHealthyChats(activeChatIds);

            // Log chats skipped due to health issues
            var skippedChats = activeChatIds.Except(actionableChatIds).ToList();
            if (skippedChats.Count > 0)
            {
                _logger.LogWarning(
                    "Skipping {Count} unhealthy chats for unban action: {ChatIds}. " +
                    "Bot lacks required permissions (admin + ban members) in these chats.",
                    skippedChats.Count,
                    string.Join(", ", skippedChats));
            }

            using var semaphore = new SemaphoreSlim(3);
            var unbanTasks = actionableChatIds.Select(async chatId =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    await botClient.UnbanChatMember(
                        chatId: chatId,
                        userId: userId,
                        onlyIfBanned: true,
                        cancellationToken: cancellationToken);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to unban user {UserId} from chat {ChatId}", userId, chatId);
                    return false;
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var unbanResults = await Task.WhenAll(unbanTasks);
            result.ChatsAffected = unbanResults.Count(success => success);

            // Record unban action
            var unbanAction = new UserActionRecord(
                Id: 0,
                UserId: userId,
                ActionType: UserActionType.Unban,
                MessageId: null,
                IssuedBy: executor,
                IssuedAt: DateTimeOffset.UtcNow,
                ExpiresAt: null,
                Reason: reason
            );
            await _userActionsRepository.InsertAsync(unbanAction, cancellationToken);

            // Optionally restore trust (for false positive corrections)
            if (restoreTrust)
            {
                await TrustUserAsync(userId, executor, "Trust restored after unban (false positive correction)", cancellationToken);
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
        Actor executor,
        string reason,
        bool restoreTrust = false,
        CancellationToken cancellationToken = default)
    {
        var botToken = await _configLoader.LoadConfigAsync();
        var botClient = _botClientFactory.GetOrCreate(botToken);
        return await UnbanUserAsync(botClient, userId, executor, reason, restoreTrust, cancellationToken);
    }

    /// <summary>
    /// Delete a message from Telegram and mark as deleted in database
    /// Used by: Messages.razor "Delete" button
    /// </summary>
    public async Task<ModerationResult> DeleteMessageAsync(
        long messageId,
        long chatId,
        long userId,
        Actor deletedBy,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = new ModerationResult();

            // Get bot client from factory (singleton instance)
            var botToken = await _configLoader.LoadConfigAsync();
            var botClient = _botClientFactory.GetOrCreate(botToken);

            // 1. Delete the message from Telegram and mark as deleted in database
            try
            {
                await _botMessageService.DeleteAndMarkMessageAsync(
                    botClient,
                    chatId,
                    (int)messageId,
                    deletionSource: "manual_ui_delete",
                    cancellationToken);
                result.MessageDeleted = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete message {MessageId} in chat {ChatId} (may already be deleted)", messageId, chatId);
            }

            // 3. Record delete action for audit trail
            var deleteAction = new UserActionRecord(
                Id: 0,
                UserId: userId,
                ActionType: UserActionType.Delete,
                MessageId: messageId,
                IssuedBy: deletedBy,
                IssuedAt: DateTimeOffset.UtcNow,
                ExpiresAt: null,
                Reason: reason ?? "Manual message deletion"
            );
            await _userActionsRepository.InsertAsync(deleteAction, cancellationToken);

            _logger.LogInformation(
                "Deleted message {MessageId} from chat {ChatId} by {DeletedBy}. Reason: {Reason}",
                messageId,
                chatId,
                deletedBy,
                reason ?? "Manual deletion");

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
    /// Temporarily ban user globally with automatic unrestriction.
    /// Used by: /tempban command
    /// </summary>
    public async Task<ModerationResult> TempBanUserAsync(
        ITelegramBotClient botClient,
        long userId,
        long? messageId,
        Actor executor,
        string reason,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        // Protect Telegram service account from moderation
        var protectionResult = CheckServiceAccountProtection(userId);
        if (protectionResult != null)
            return protectionResult;

        try
        {
            var result = new ModerationResult();
            var expiresAt = DateTimeOffset.UtcNow.Add(duration);

            // Temp ban globally (permanent ban, will be lifted by background job) (PERF-TG-2: parallel execution)
            var allChats = await _managedChatsRepository.GetAllChatsAsync(cancellationToken);
            var activeChatIds = allChats.Where(c => c.IsActive).Select(c => c.ChatId).ToList();

            // Health gate: Filter for chats where bot has confirmed permissions
            var actionableChatIds = _chatManagementService.FilterHealthyChats(activeChatIds);

            // Log chats skipped due to health issues
            var skippedChats = activeChatIds.Except(actionableChatIds).ToList();
            if (skippedChats.Count > 0)
            {
                _logger.LogWarning(
                    "Skipping {Count} unhealthy chats for temp ban action: {ChatIds}. " +
                    "Bot lacks required permissions (admin + ban members) in these chats.",
                    skippedChats.Count,
                    string.Join(", ", skippedChats));
            }

            using var semaphore = new SemaphoreSlim(3);
            var banTasks = actionableChatIds.Select(async chatId =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    await botClient.BanChatMember(
                        chatId: chatId,
                        userId: userId,
                        cancellationToken: cancellationToken);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to temp ban user {UserId} from chat {ChatId}", userId, chatId);
                    return false;
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var banResults = await Task.WhenAll(banTasks);
            result.ChatsAffected = banResults.Count(success => success);

            // Record temp ban action
            var banAction = new UserActionRecord(
                Id: 0,
                UserId: userId,
                ActionType: UserActionType.Ban,
                MessageId: messageId,
                IssuedBy: executor,
                IssuedAt: DateTimeOffset.UtcNow,
                ExpiresAt: expiresAt,
                Reason: reason
            );
            await _userActionsRepository.InsertAsync(banAction, cancellationToken);

            // Schedule automatic unrestriction via Quartz.NET background job
            var payload = new TempbanExpiryJobPayload(
                UserId: userId,
                Reason: reason,
                ExpiresAt: expiresAt
            );

            var delaySeconds = (int)(expiresAt - DateTimeOffset.UtcNow).TotalSeconds;
            var jobId = await _jobScheduler.ScheduleJobAsync(
                "TempbanExpiry",
                payload,
                delaySeconds: delaySeconds,
                cancellationToken);

            _logger.LogInformation(
                "Successfully scheduled TempbanExpiryJob for user {UserId} (JobId: {JobId}, Expires: {ExpiresAt})",
                userId,
                jobId,
                expiresAt);

            // Send DM notification with rejoin links (both UI and bot command)
            await SendTempBanNotificationAsync(botClient, userId, reason, duration, cancellationToken);

            _logger.LogInformation(
                "Temp ban action completed: User {UserId} banned from {ChatsAffected} chats until {ExpiresAt}",
                userId, result.ChatsAffected, expiresAt);

            result.Success = true;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to temp ban user {UserId}", userId);
            return new ModerationResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// UI-friendly overload: Temp ban user without requiring bot client parameter
    /// </summary>
    public async Task<ModerationResult> TempBanUserAsync(
        long userId,
        long? messageId,
        Actor executor,
        string reason,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        var botToken = await _configLoader.LoadConfigAsync();
        var botClient = _botClientFactory.GetOrCreate(botToken);
        return await TempBanUserAsync(botClient, userId, messageId, executor, reason, duration, cancellationToken);
    }

    /// <summary>
    /// Restrict user (mute) globally with automatic unrestriction.
    /// Used by: /mute command
    /// </summary>
    public async Task<ModerationResult> RestrictUserAsync(
        ITelegramBotClient botClient,
        long userId,
        long? messageId,
        Actor executor,
        string reason,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        // Protect Telegram service account from moderation
        var protectionResult = CheckServiceAccountProtection(userId);
        if (protectionResult != null)
            return protectionResult;

        try
        {
            var result = new ModerationResult();
            var expiresAt = DateTimeOffset.UtcNow.Add(duration);

            // Restrict user globally (Telegram API handles auto-unrestrict via until_date) (PERF-TG-2: parallel execution)
            var allChats = await _managedChatsRepository.GetAllChatsAsync(cancellationToken);
            var activeChatIds = allChats.Where(c => c.IsActive).Select(c => c.ChatId).ToList();

            // Health gate: Filter for chats where bot has confirmed permissions
            var actionableChatIds = _chatManagementService.FilterHealthyChats(activeChatIds);

            // Log chats skipped due to health issues
            var skippedChats = activeChatIds.Except(actionableChatIds).ToList();
            if (skippedChats.Count > 0)
            {
                _logger.LogWarning(
                    "Skipping {Count} unhealthy chats for restrict action: {ChatIds}. " +
                    "Bot lacks required permissions (admin + ban members) in these chats.",
                    skippedChats.Count,
                    string.Join(", ", skippedChats));
            }

            using var semaphore = new SemaphoreSlim(3);
            var restrictTasks = actionableChatIds.Select(async chatId =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    await botClient.RestrictChatMember(
                        chatId: chatId,
                        userId: userId,
                        permissions: new global::Telegram.Bot.Types.ChatPermissions
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
                        },
                        untilDate: expiresAt.DateTime,
                        cancellationToken: cancellationToken);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to restrict user {UserId} in chat {ChatId}", userId, chatId);
                    return false;
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var restrictResults = await Task.WhenAll(restrictTasks);
            result.ChatsAffected = restrictResults.Count(success => success);

            // Record mute action
            var muteAction = new UserActionRecord(
                Id: 0,
                UserId: userId,
                ActionType: UserActionType.Mute,
                MessageId: messageId,
                IssuedBy: executor,
                IssuedAt: DateTimeOffset.UtcNow,
                ExpiresAt: expiresAt,
                Reason: reason
            );
            await _userActionsRepository.InsertAsync(muteAction, cancellationToken);

            _logger.LogInformation(
                "Mute action completed: User {UserId} restricted in {ChatsAffected} chats until {ExpiresAt}",
                userId, result.ChatsAffected, expiresAt);

            result.Success = true;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restrict user {UserId}", userId);
            return new ModerationResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// Send DM notification to tempbanned user with rejoin links for all managed chats.
    /// Used by both UI and bot command tempban actions.
    /// No chat fallback - DM only per requirements.
    /// </summary>
    private async Task SendTempBanNotificationAsync(
        ITelegramBotClient botClient,
        long userId,
        string reason,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Starting tempban notification for user {UserId}. Reason: {Reason}, Duration: {Duration}",
                userId, reason, duration);

            var expiresAt = DateTimeOffset.UtcNow.Add(duration);

            // Get all active managed chats to provide rejoin links
            using var scope = _serviceProvider.CreateScope();
            var managedChatsRepo = scope.ServiceProvider.GetRequiredService<IManagedChatsRepository>();
            var allChats = await managedChatsRepo.GetAllChatsAsync(cancellationToken);
            var activeChats = allChats.Where(c => c.IsActive).ToList();

            _logger.LogInformation(
                "Found {ChatCount} active managed chats for tempban notification",
                activeChats.Count);

            // Build notification message
            var notification = $"⏱️ **You have been temporarily banned**\n\n" +
                              $"**Reason:** {reason}\n" +
                              $"**Duration:** {TimeSpanUtilities.FormatDuration(duration)}\n" +
                              $"**Expires:** {expiresAt:yyyy-MM-dd HH:mm} UTC\n\n" +
                              $"You will be automatically unbanned after this time.";

            // Collect invite links for all active chats
            var inviteLinks = new List<(string ChatName, string? InviteLink)>();
            foreach (var chat in activeChats)
            {
                var inviteLink = await _inviteLinkService.GetInviteLinkAsync(botClient, chat.ChatId, cancellationToken);
                inviteLinks.Add((chat.ChatName ?? $"Chat {chat.ChatId}", inviteLink));

                if (inviteLink != null)
                {
                    _logger.LogInformation(
                        "Retrieved invite link for chat {ChatId} ({ChatName}): {InviteLink}",
                        chat.ChatId, chat.ChatName, inviteLink);
                }
                else
                {
                    _logger.LogWarning(
                        "Failed to retrieve invite link for chat {ChatId} ({ChatName}). User will not receive rejoin link for this chat.",
                        chat.ChatId, chat.ChatName);
                }
            }

            // Add rejoin links section if any links were retrieved
            var linksWithValues = inviteLinks.Where(l => l.InviteLink != null).ToList();
            if (linksWithValues.Any())
            {
                notification += "\n\n**Rejoin Links:**";
                foreach (var (chatName, inviteLink) in linksWithValues)
                {
                    notification += $"\n• [{chatName}]({inviteLink})";
                }
            }
            else
            {
                _logger.LogWarning(
                    "No invite links could be retrieved for any chat. User will not receive rejoin links.");
            }

            // Send DM only (no chat fallback per user requirements)
            // Use first active chat as fallback chat ID (required by SendToUserAsync for potential chat mentions)
            var fallbackChatId = activeChats.FirstOrDefault()?.ChatId ?? 0;

            _logger.LogInformation(
                "Attempting to send tempban DM to user {UserId}. Notification length: {Length} characters",
                userId, notification.Length);

            var result = await _messagingService.SendToUserAsync(
                botClient,
                userId: userId,
                chatId: fallbackChatId,
                messageText: notification,
                replyToMessageId: null,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Tempban notification delivery result for user {UserId}: Success={Success}, DeliveryMethod={DeliveryMethod}, Error={Error}",
                userId, result.Success, result.DeliveryMethod, result.ErrorMessage ?? "None");

            if (result.DeliveryMethod == MessageDeliveryMethod.PrivateDm)
            {
                _logger.LogInformation(
                    "✅ Successfully sent tempban notification via DM to user {UserId} with {LinkCount} rejoin links",
                    userId,
                    linksWithValues.Count);
            }
            else if (result.DeliveryMethod == MessageDeliveryMethod.ChatMention)
            {
                _logger.LogInformation(
                    "⚠️ Tempban notification sent via chat mention fallback for user {UserId} (DM unavailable)",
                    userId);
            }
            else
            {
                _logger.LogWarning(
                    "❌ Failed to send tempban notification to user {UserId}. DeliveryMethod: {DeliveryMethod}",
                    userId, result.DeliveryMethod);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to send tempban notification to user {UserId}",
                userId);
            // Non-fatal - tempban still succeeded
        }
    }

    /// <summary>
    /// Send DM notification to user about receiving a warning
    /// </summary>
    private async Task SendWarningNotificationAsync(
        long userId,
        int warningCount,
        string reason,
        CancellationToken cancellationToken)
    {
        try
        {
            var message = $"⚠️ **Warning Issued**\n\n" +
                         $"You have received a warning.\n\n" +
                         $"**Reason:** {reason}\n" +
                         $"**Total Warnings:** {warningCount}\n\n" +
                         $"Please review the group rules and avoid similar behavior in the future.\n\n" +
                         $"💡 Use /mystatus to check your current status.";

            var notification = new Notification("warning", message);

            await _notificationOrchestrator.SendTelegramDmAsync(
                userId,
                notification,
                cancellationToken);
        }
        catch (Exception ex)
        {
            // Don't fail the warning if notification fails
            _logger.LogWarning(
                ex,
                "Failed to send warning notification to user {UserId}",
                userId);
        }
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
    public int WarningCount { get; set; }
    public bool AutoBanTriggered { get; set; }
}
