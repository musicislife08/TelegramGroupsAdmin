using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.BotCommands;

namespace TelegramGroupsAdmin.Telegram.Services.BackgroundServices;

/// <summary>
/// Handles message processing: new messages, edits, spam detection
/// REFACTOR-1: Orchestrates specialized handlers (media, file scanning, translation)
/// REFACTOR-2: Additional handlers for image processing and background job scheduling
/// </summary>
public partial class MessageProcessingService(
    IServiceScopeFactory scopeFactory,
    IOptions<MessageHistoryOptions> historyOptions,
    CommandRouter commandRouter,
    ChatManagementService chatManagementService,
    TelegramPhotoService telegramPhotoService,
    TelegramMediaService telegramMediaService,
    IServiceProvider serviceProvider,
    ILogger<MessageProcessingService> logger)
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly MessageHistoryOptions _historyOptions = historyOptions.Value;
    private readonly TelegramPhotoService _photoService = telegramPhotoService;
    private readonly TelegramMediaService _mediaService = telegramMediaService;

    // REFACTOR-1: Specialized handlers injected via scoped services (created per request)
    // These are NOT injected in constructor since MessageProcessingService is Singleton
    // Instead, resolved from scope when needed

    // Events for real-time UI updates
    public event Action<MessageRecord>? OnNewMessage;
    public event Action<MessageEditRecord>? OnMessageEdited;
    public event Action<long, TelegramGroupsAdmin.Telegram.Models.MediaType>? OnMediaUpdated;

    /// <summary>
    /// Raises the OnMediaUpdated event (called by MediaRefetchWorkerService)
    /// </summary>
    public void RaiseMediaUpdated(long messageId, TelegramGroupsAdmin.Telegram.Models.MediaType mediaType)
    {
        OnMediaUpdated?.Invoke(messageId, mediaType);
    }

    /// <summary>
    /// Handle new messages: save to database, execute commands, run spam detection
    /// Only processes group/supergroup messages - private DMs are handled by commands only
    /// </summary>
    public async Task HandleNewMessageAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken = default)
    {
        // Skip private chats - only process group messages for history/spam detection
        if (message.Chat.Type == ChatType.Private)
        {
            // Private DMs are only for bot commands (/start, /help, etc)
            // Execute command if present, but don't save to message history
            if (commandRouter.IsCommand(message))
            {
                try
                {
                    var commandResult = await commandRouter.RouteCommandAsync(botClient, message, cancellationToken);
                    if (commandResult?.Response != null && !string.IsNullOrWhiteSpace(commandResult.Response))
                    {
                        // Use BotMessageService to save bot response to database
                        using var scope = _scopeFactory.CreateScope();
                        var botMessageService = scope.ServiceProvider.GetRequiredService<BotMessageService>();
                        await botMessageService.SendAndSaveMessageAsync(
                            botClient,
                            message.Chat.Id,
                            commandResult.Response,
                            parseMode: ParseMode.Markdown,
                            cancellationToken: cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error executing command in private chat {ChatId}", message.Chat.Id);
                }
            }
            return; // Don't process private messages further
        }

        // Detect Group → Supergroup migration
        // When a Group is upgraded to Supergroup (e.g., when granting admin), Telegram:
        // 1. Creates a new Supergroup with different chat ID
        // 2. Sends a migration message with MigrateToChatId pointing to new chat
        if (message.MigrateToChatId.HasValue)
        {
            var oldChatId = message.Chat.Id;
            var newChatId = message.MigrateToChatId.Value;

            logger.LogWarning(
                "🔄 Chat migration detected: Group {OldChatId} upgraded to Supergroup {NewChatId}",
                oldChatId,
                newChatId);

            await chatManagementService.HandleChatMigrationAsync(oldChatId, newChatId, cancellationToken);
            return; // Don't process migration message further
        }

        // Delete service messages (join/leave, photo changes, title changes, etc.)
        // These are automatically generated by Telegram and clutter the chat
        // Use property-based detection instead of MessageType enum for broader coverage
        var isServiceMessage = message.NewChatMembers != null ||
                               message.LeftChatMember != null ||
                               message.NewChatPhoto != null ||
                               message.DeleteChatPhoto == true ||
                               message.NewChatTitle != null ||
                               message.PinnedMessage != null ||
                               message.GroupChatCreated == true ||
                               message.SupergroupChatCreated == true ||
                               message.ChannelChatCreated == true;

        if (isServiceMessage)
        {
            logger.LogInformation(
                "Detected service message: Type={Type}, MessageId={MessageId}, ChatId={ChatId}",
                message.Type,
                message.MessageId,
                message.Chat.Id);

            try
            {
                // Use BotMessageService for tracked deletion
                using var scope = _scopeFactory.CreateScope();
                var botMessageService = scope.ServiceProvider.GetRequiredService<BotMessageService>();
                await botMessageService.DeleteAndMarkMessageAsync(
                    botClient,
                    message.Chat.Id,
                    message.MessageId,
                    deletionSource: "service_message",
                    cancellationToken);

                logger.LogInformation(
                    "Deleted service message (type: {Type}) in chat {ChatId}",
                    message.Type,
                    message.Chat.Id);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to delete service message {MessageId} in chat {ChatId}",
                    message.MessageId,
                    message.Chat.Id);
            }
            return; // Don't process service messages further
        }

        // Process messages from all group chats where bot is added
        try
        {
            var now = DateTimeOffset.UtcNow;

            // Update last_seen_at for this chat (also auto-adds chat if it doesn't exist)
            // Then refresh admin cache for newly discovered chats OR chats with empty admin cache
            using var scope = serviceProvider.CreateScope();
            var managedChatsRepository = scope.ServiceProvider.GetRequiredService<IManagedChatsRepository>();
            var existingChat = await managedChatsRepository.GetByChatIdAsync(message.Chat.Id, cancellationToken);
            var isNewChat = existingChat == null;

            await managedChatsRepository.UpdateLastSeenAsync(message.Chat.Id, now, cancellationToken);

            // Check if we need to refresh admin cache (only for groups/supergroups, not private DMs)
            if (message.Chat.Type is ChatType.Group or ChatType.Supergroup)
            {
                bool shouldRefreshAdmins = false;
                string refreshReason = "";

                if (isNewChat)
                {
                    shouldRefreshAdmins = true;
                    refreshReason = "newly discovered chat";
                }
                else
                {
                    // Check if admin cache is empty for this chat
                    var chatAdminsRepository = scope.ServiceProvider.GetRequiredService<IChatAdminsRepository>();
                    var adminCount = await chatAdminsRepository.GetAdminCountAsync(message.Chat.Id, cancellationToken);

                    if (adminCount == 0)
                    {
                        shouldRefreshAdmins = true;
                        refreshReason = "admin cache is empty";
                    }
                }

                if (shouldRefreshAdmins)
                {
                    logger.LogInformation(
                        "Refreshing admin cache for chat {ChatId} ({Reason})",
                        message.Chat.Id,
                        refreshReason);

                    try
                    {
                        await chatManagementService.RefreshChatAdminsAsync(botClient, message.Chat.Id, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to refresh admins for chat {ChatId}", message.Chat.Id);
                    }
                }
            }
            else
            {
                if (isNewChat)
                {
                    logger.LogDebug("Discovered new private chat {ChatId}, skipping admin cache refresh", message.Chat.Id);
                }
            }

            // Check if message is a bot command (execute first, then save to history)
            // Note: Errors are caught to ensure message history is always saved
            CommandResult? commandResult = null;
            if (commandRouter.IsCommand(message))
            {
                try
                {
                    commandResult = await commandRouter.RouteCommandAsync(botClient, message, cancellationToken);
                    if (commandResult != null)
                    {
                        // Send response if there is one (and it's not empty)
                        if (commandResult.Response != null && !string.IsNullOrWhiteSpace(commandResult.Response))
                        {
                            // Use BotMessageService to save bot response to database
                            var botMessageService = scope.ServiceProvider.GetRequiredService<BotMessageService>();
                            var responseMessage = await botMessageService.SendAndSaveMessageAsync(
                                botClient,
                                message.Chat.Id,
                                commandResult.Response,
                                parseMode: ParseMode.Markdown,
                                replyParameters: new ReplyParameters { MessageId = message.MessageId },
                                cancellationToken: cancellationToken);

                            // REFACTOR-2: Schedule auto-delete if requested using BackgroundJobScheduler
                            if (commandResult.DeleteResponseAfterSeconds.HasValue)
                            {
                                var jobScheduler = scope.ServiceProvider.GetRequiredService<TelegramGroupsAdmin.Telegram.Handlers.BackgroundJobScheduler>();
                                await jobScheduler.ScheduleMessageDeleteAsync(
                                    message.Chat.Id,
                                    responseMessage.MessageId,
                                    commandResult.DeleteResponseAfterSeconds.Value,
                                    "command_response",
                                    cancellationToken);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex,
                        "Error executing command in chat {ChatId}, message will still be saved to history",
                        message.Chat.Id);
                    // Continue to save message to history regardless of command error
                }
                // Continue to save command message to history (don't return early)
            }

            // Check for @admin mentions (independent of commands)
            // Note: Errors are caught to ensure message history is always saved
            var text = message.Text ?? message.Caption;
            if (!string.IsNullOrWhiteSpace(text))
            {
                try
                {
                    using var mentionScope = serviceProvider.CreateScope();
                    var adminMentionHandler = mentionScope.ServiceProvider.GetRequiredService<AdminMentionHandler>();
                    if (adminMentionHandler.ContainsAdminMention(text))
                    {
                        await adminMentionHandler.NotifyAdminsAsync(botClient, message, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex,
                        "Error handling @admin mention in chat {ChatId}, message will still be saved to history",
                        message.Chat.Id);
                    // Continue to save message to history regardless of error
                }
            }

            // Extract URLs from message text
            var urls = UrlUtilities.ExtractUrls(text);

            // REFACTOR-2: Use ImageProcessingHandler for photo detection and processing
            string? photoFileId = null;
            int? photoFileSize = null;
            string? photoLocalPath = null;
            string? photoThumbnailPath = null;

            var imageHandler = scope.ServiceProvider.GetRequiredService<TelegramGroupsAdmin.Telegram.Handlers.ImageProcessingHandler>();
            var imageResult = await imageHandler.ProcessImageAsync(
                botClient,
                message,
                message.Chat.Id,
                message.MessageId,
                cancellationToken);

            if (imageResult != null)
            {
                photoFileId = imageResult.FileId;
                photoFileSize = imageResult.FileSize;
                photoLocalPath = imageResult.FullPath;
                photoThumbnailPath = imageResult.ThumbnailPath;
            }

            // REFACTOR-1: Use MediaProcessingHandler for media detection and download
            MediaType? mediaType = null;
            string? mediaFileId = null;
            long? mediaFileSize = null;
            string? mediaFileName = null;
            string? mediaMimeType = null;
            string? mediaLocalPath = null;
            int? mediaDuration = null;

            var mediaHandler = scope.ServiceProvider.GetRequiredService<TelegramGroupsAdmin.Telegram.Handlers.MediaProcessingHandler>();
            var mediaResult = await mediaHandler.ProcessMediaAsync(
                message,
                message.Chat.Id,
                message.MessageId,
                cancellationToken);

            if (mediaResult != null)
            {
                mediaType = mediaResult.MediaType;
                mediaFileId = mediaResult.FileId;
                mediaFileSize = mediaResult.FileSize;
                mediaFileName = mediaResult.FileName;
                mediaMimeType = mediaResult.MimeType;
                mediaDuration = mediaResult.Duration;
                mediaLocalPath = mediaResult.LocalPath; // Null for Documents (metadata-only)
            }

            // REFACTOR-1: Use FileScanningHandler for document scanning
            var fileScanHandler = scope.ServiceProvider.GetRequiredService<TelegramGroupsAdmin.Telegram.Handlers.FileScanningHandler>();
            await fileScanHandler.ProcessFileScanningAsync(
                message,
                message.Chat.Id,
                message.From!.Id,
                cancellationToken);

            // Calculate content hash for spam correlation
            var urlsJson = urls != null ? JsonSerializer.Serialize(urls) : "";
            var contentHash = HashUtilities.ComputeContentHash(text ?? "", urlsJson);

            // Check if chat icon is cached on disk
            var chatIconFileName = $"{Math.Abs(message.Chat.Id)}.jpg";
            var chatIconCachedPath = Path.Combine(_historyOptions.ImageStoragePath, "media", "chat_icons", chatIconFileName);
            var chatIconPath = File.Exists(chatIconCachedPath) ? $"chat_icons/{chatIconFileName}" : null;

            // REFACTOR-1: Use TranslationHandler for translation detection and processing
            MessageTranslation? translation = null;
            if (!string.IsNullOrWhiteSpace(text))
            {
                var translationHandler = scope.ServiceProvider.GetRequiredService<TelegramGroupsAdmin.Telegram.Handlers.TranslationHandler>();
                var translationResult = await translationHandler.ProcessTranslationAsync(
                    text,
                    message.MessageId,
                    cancellationToken);

                if (translationResult != null)
                {
                    translation = translationResult.Translation;
                }
            }

            // Determine spam check skip reason (before saving message)
            // Check if user is trusted or admin to set appropriate skip reason
            var spamCheckSkipReason = SpamCheckSkipReason.NotSkipped; // Default: spam checks will run

            if (message.From?.Id != null)
            {
                using var skipReasonScope = serviceProvider.CreateScope();
                var userActionsRepository = skipReasonScope.ServiceProvider.GetRequiredService<IUserActionsRepository>();
                var chatAdminsRepository = skipReasonScope.ServiceProvider.GetRequiredService<IChatAdminsRepository>();

                // Check admin status first (higher priority)
                bool isUserAdmin = await chatAdminsRepository.IsAdminAsync(message.Chat.Id, message.From.Id, cancellationToken);
                if (isUserAdmin)
                {
                    spamCheckSkipReason = SpamCheckSkipReason.UserAdmin;
                }
                else
                {
                    // Check trust status if not admin
                    bool isUserTrusted = await userActionsRepository.IsUserTrustedAsync(
                        message.From.Id,
                        message.Chat.Id,
                        cancellationToken);

                    if (isUserTrusted)
                    {
                        spamCheckSkipReason = SpamCheckSkipReason.UserTrusted;
                    }
                }
            }

            // User photo will be fetched asynchronously after message save (non-blocking)
            var messageRecord = new MessageRecord(
                message.MessageId,
                message.From!.Id,
                message.From.Username,
                message.From.FirstName,
                message.Chat.Id,
                now,
                text,
                photoFileId,
                photoFileSize,
                urlsJson != "" ? urlsJson : null,
                EditDate: message.EditDate.HasValue ? new DateTimeOffset(message.EditDate.Value, TimeSpan.Zero) : null,
                ContentHash: contentHash,
                ChatName: message.Chat.Title ?? message.Chat.Username,
                PhotoLocalPath: photoLocalPath,
                PhotoThumbnailPath: photoThumbnailPath,
                ChatIconPath: chatIconPath,
                UserPhotoPath: null, // Will be populated by background task
                DeletedAt: null,
                DeletionSource: null,
                ReplyToMessageId: message.ReplyToMessage?.MessageId,
                ReplyToUser: null, // Populated by repository queries via JOIN
                ReplyToText: null, // Populated by repository queries via JOIN
                                   // Media attachment fields (Phase 4.X)
                MediaType: mediaType,
                MediaFileId: mediaFileId,
                MediaFileSize: mediaFileSize,
                MediaFileName: mediaFileName,
                MediaMimeType: mediaMimeType,
                MediaLocalPath: mediaLocalPath,
                MediaDuration: mediaDuration,
                // Translation (Phase 4.20)
                Translation: translation,
                // Spam check skip reason
                SpamCheckSkipReason: spamCheckSkipReason
            );

            // Save message to database using a scoped repository
            using var messageScope = _scopeFactory.CreateScope();
            var repository = messageScope.ServiceProvider.GetRequiredService<IMessageHistoryRepository>();
            await repository.InsertMessageAsync(messageRecord, cancellationToken);

            // Save translation to database if present
            if (translation != null)
            {
                var translationService = messageScope.ServiceProvider.GetRequiredService<IMessageTranslationService>();
                await translationService.InsertTranslationAsync(translation, cancellationToken);
                logger.LogDebug(
                    "Saved translation for message {MessageId} ({Language})",
                    message.MessageId,
                    translation.DetectedLanguage);
            }

            // Enrich message with URL previews if URLs are present (reuse already-extracted URLs from line 292)
            // This runs AFTER message save but BEFORE content detection so all algorithms benefit
            if (urls != null && urls.Any())
            {
                var urlScrapingService = messageScope.ServiceProvider.GetRequiredService<TelegramGroupsAdmin.ContentDetection.Services.IUrlContentScrapingService>();
                var enrichedText = await urlScrapingService.EnrichMessageWithUrlPreviewsAsync(text!, cancellationToken);

                if (enrichedText != text) // Content was enriched
                {
                    await repository.UpdateMessageTextAsync(message.MessageId, enrichedText, cancellationToken);
                    text = enrichedText; // Use enriched text for content detection later

                    logger.LogDebug(
                        "Enriched message {MessageId} with URL previews ({UrlCount} URLs)",
                        message.MessageId,
                        urls.Count);
                }
            }

            // Upsert user into telegram_users table (centralized user tracking)
            var telegramUserRepo = messageScope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
            var telegramUser = new TelegramGroupsAdmin.Telegram.Models.TelegramUser(
                TelegramUserId: message.From!.Id,
                Username: message.From.Username,
                FirstName: message.From.FirstName,
                LastName: message.From.LastName,
                UserPhotoPath: null, // Will be populated by FetchUserPhotoJob
                PhotoHash: null,
                PhotoFileUniqueId: null, // Will be populated by FetchUserPhotoJob
                IsBot: message.From.IsBot, // Track bot status from Telegram API
                IsTrusted: false,
                BotDmEnabled: false, // Will be set to true when user sends /start in private chat
                FirstSeenAt: now,
                LastSeenAt: now,
                CreatedAt: now,
                UpdatedAt: now
            );
            await telegramUserRepo.UpsertAsync(telegramUser, cancellationToken);

            // Phase 4.10: Check for impersonation (name + photo similarity vs admins)
            // Check users on their first N messages
            var impersonationService = messageScope.ServiceProvider.GetRequiredService<IImpersonationDetectionService>();
            var shouldCheck = await impersonationService.ShouldCheckUserAsync(message.From!.Id, message.Chat.Id);

            if (shouldCheck)
            {
                logger.LogDebug(
                    "Checking user {UserId} for impersonation on message #{MessageCount}",
                    message.From.Id,
                    message.MessageId);

                // Get cached user photo path (may be null if not fetched yet)
                var existingUser = await telegramUserRepo.GetByTelegramIdAsync(message.From.Id, cancellationToken);
                var photoPath = existingUser?.UserPhotoPath;

                // Check for impersonation
                var impersonationResult = await impersonationService.CheckUserAsync(
                    message.From.Id,
                    message.Chat.Id,
                    message.From.FirstName,
                    message.From.LastName,
                    photoPath);

                if (impersonationResult != null)
                {
                    logger.LogWarning(
                        "Impersonation detected for user {UserId} in chat {ChatId} (score: {Score}, risk: {Risk})",
                        message.From.Id,
                        message.Chat.Id,
                        impersonationResult.TotalScore,
                        impersonationResult.RiskLevel);

                    // Execute action (create alert, auto-ban if score >= 100)
                    await impersonationService.ExecuteActionAsync(impersonationResult);

                    // If auto-banned, message will remain in history for audit trail
                    // User will be banned from all chats immediately
                    if (impersonationResult.ShouldAutoBan)
                    {
                        logger.LogInformation(
                            "User {UserId} auto-banned for impersonation (score: {Score})",
                            message.From.Id,
                            impersonationResult.TotalScore);
                    }
                }
            }

            // Raise event for real-time UI updates
            OnNewMessage?.Invoke(messageRecord);

            logger.LogDebug(
                "Cached message {MessageId} from user {UserId} in chat {ChatId} (photo: {HasPhoto}, text: {HasText})",
                message.MessageId,
                message.From.Id,
                message.Chat.Id,
                photoFileId != null,
                text != null);

            // Delete command message if requested (AFTER saving to database for FK integrity)
            if (commandResult?.DeleteCommandMessage == true)
            {
                try
                {
                    // Use BotMessageService for tracked deletion
                    var botMessageService = scope.ServiceProvider.GetRequiredService<BotMessageService>();
                    await botMessageService.DeleteAndMarkMessageAsync(
                        botClient,
                        message.Chat.Id,
                        message.MessageId,
                        deletionSource: "command_cleanup",
                        cancellationToken);

                    logger.LogDebug("Deleted command message {MessageId} in chat {ChatId}", message.MessageId, message.Chat.Id);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to delete command message {MessageId} in chat {ChatId}", message.MessageId, message.Chat.Id);
                }
            }

            // REFACTOR-2: Fetch user profile photo via TickerQ using BackgroundJobScheduler
            if (message.From?.Id != null)
            {
                var jobScheduler = scope.ServiceProvider.GetRequiredService<TelegramGroupsAdmin.Telegram.Handlers.BackgroundJobScheduler>();
                await jobScheduler.ScheduleUserPhotoFetchAsync(message.MessageId, message.From.Id, cancellationToken);
            }

            // REFACTOR-2: Automatic content detection using ContentDetectionOrchestrator
            // Skip content detection for command messages (already processed by CommandRouter)
            // Run content detection if message has text OR images (image-only spam detection)
            if (commandResult == null && (!string.IsNullOrWhiteSpace(text) || photoLocalPath != null))
            {
                _ = Task.Run(async () =>
                {
                    using var detectionScope = serviceProvider.CreateScope();
                    var contentOrchestrator = detectionScope.ServiceProvider.GetRequiredService<TelegramGroupsAdmin.Telegram.Handlers.ContentDetectionOrchestrator>();

                    // Use translated text if available (avoids double translation in ContentDetectionEngine)
                    var textForDetection = translation?.TranslatedText ?? text;

                    await contentOrchestrator.RunDetectionAsync(
                        botClient,
                        message,
                        textForDetection,
                        photoLocalPath,
                        editVersion: 0,
                        CancellationToken.None);
                }, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Error caching message {MessageId} from user {UserId} in chat {ChatId}",
                message.MessageId,
                message.From?.Id,
                message.Chat?.Id);
        }
    }

    /// <summary>
    /// REFACTOR-2: Handle edited messages using MessageEditProcessor
    /// </summary>
    public async Task HandleEditedMessageAsync(ITelegramBotClient botClient, Message editedMessage, CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var editProcessor = scope.ServiceProvider.GetRequiredService<TelegramGroupsAdmin.Telegram.Handlers.MessageEditProcessor>();

            var editRecord = await editProcessor.ProcessEditAsync(botClient, editedMessage, scope, cancellationToken);

            // Raise event for real-time UI updates (if edit actually occurred)
            if (editRecord != null)
            {
                OnMessageEdited?.Invoke(editRecord);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Error handling edit for message {MessageId}",
                editedMessage.MessageId);
        }
    }

    // REFACTOR-2: Extracted methods to specialized handlers
    //   Phase 1: ImageProcessingHandler, BackgroundJobScheduler
    //   Phase 2: SpamDetectionOrchestrator, LanguageWarningHandler
    //   Phase 3: MessageEditProcessor

    /// <summary>
    /// Schedule file scanning via TickerQ with 0s delay for instant execution
    /// Phase 4.14: Downloads file to temp, scans with ClamAV+VirusTotal, deletes if infected
    /// Temp file deleted after scan (no persistent storage)
    /// </summary>
    private async Task ScheduleFileScanJobAsync(
        long messageId,
        long chatId,
        long userId,
        string fileId,
        long fileSize,
        string? fileName,
        string? contentType,
        CancellationToken cancellationToken = default)
    {
        var scanPayload = new TelegramGroupsAdmin.Telegram.Abstractions.Jobs.FileScanJobPayload(
            MessageId: messageId,
            ChatId: chatId,
            UserId: userId,
            FileId: fileId,
            FileSize: fileSize,
            FileName: fileName,
            ContentType: contentType
        );

        logger.LogInformation(
            "Scheduling file scan for '{FileName}' ({FileSize} bytes) from user {UserId} in chat {ChatId}",
            fileName ?? "unknown",
            fileSize,
            userId,
            chatId);

        await TickerQUtilities.ScheduleJobAsync(
            serviceProvider,
            logger,
            "FileScan",
            scanPayload,
            delaySeconds: 0,
            retries: 3); // Higher retries than photo fetch (ClamAV restart, VT rate limit scenarios)
    }

}
