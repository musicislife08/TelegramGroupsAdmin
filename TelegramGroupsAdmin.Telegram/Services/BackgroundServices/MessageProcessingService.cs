using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.Telemetry;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Helpers;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.BotCommands;
using TelegramGroupsAdmin.Telegram.Services.Identity;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using TelegramGroupsAdmin.Telegram.Services.UserApi;
using TelegramGroupsAdmin.Configuration.Services;

namespace TelegramGroupsAdmin.Telegram.Services.BackgroundServices;

/// <summary>
/// Handles message processing: new messages, edits, spam detection
/// REFACTOR-1: Orchestrates specialized handlers (media, file scanning, translation)
/// REFACTOR-2: Additional handlers for image processing and background job scheduling
/// </summary>
public partial class MessageProcessingService(
    IServiceScopeFactory scopeFactory,
    IOptions<AppOptions> appOptions,
    CommandRouter commandRouter,
    IChatCache chatCache,
    IServiceProvider serviceProvider,
    PipelineMetrics pipelineMetrics,
    ChatMetrics chatMetrics,
    ILogger<MessageProcessingService> logger) : IMessageProcessingService
{
    // REFACTOR-1: Specialized handlers injected via scoped services (created per request)
    // These are NOT injected in constructor since MessageProcessingService is Singleton
    // Instead, resolved from scope when needed

    // Events for real-time UI updates
    public event Action<MessageRecord>? OnNewMessage;
    public event Action<MessageEditRecord>? OnMessageEdited;
    public event Action<int, MediaType>? OnMediaUpdated;

    /// <summary>
    /// Raises the OnMediaUpdated event (called by MediaRefetchWorkerService)
    /// </summary>
    public void RaiseMediaUpdated(int messageId, MediaType mediaType)
    {
        OnMediaUpdated?.Invoke(messageId, mediaType);
    }

    /// <summary>
    /// Handle new messages: save to database, execute commands, run spam detection
    /// Only processes group/supergroup messages - private DMs are handled by commands only
    /// </summary>
    public async Task HandleNewMessageAsync(Message message, CancellationToken cancellationToken = default)
    {
        using var activity = TelemetryConstants.MessageProcessing.StartActivity("message_processing.handle_new_message");
        activity?.SetTag("message.chat_id", message.Chat.Id);
        activity?.SetTag("message.message_id", message.MessageId);
        activity?.SetTag("message.chat_type", message.Chat.Type.ToString());
        activity?.SetTag("message.has_text", !string.IsNullOrWhiteSpace(message.Text ?? message.Caption));

        var startTimestamp = Stopwatch.GetTimestamp();

        // Skip private chats - only process group messages for history/spam detection
        if (message.Chat.Type == ChatType.Private)
        {
            // Private DMs: handle commands first
            if (commandRouter.IsCommand(message))
            {
                try
                {
                    var commandResult = await commandRouter.RouteCommandAsync(message, cancellationToken);
                    if (commandResult != null && !string.IsNullOrWhiteSpace(commandResult.Message.Text))
                    {
                        // Use BotMessageService to save bot response to database
                        using var scope = scopeFactory.CreateScope();
                        var botMessageService = scope.ServiceProvider.GetRequiredService<IBotMessageService>();
                        await botMessageService.SendAndSaveMessageAsync(
                            message.Chat.Id,
                            commandResult.Message,
                            cancellationToken: cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error executing command in private chat {Chat}", message.Chat.ToLogDebug());
                }
                return;
            }

            // Not a command - check for active exam session awaiting open-ended answer
            if (!string.IsNullOrWhiteSpace(message.Text) && message.From != null)
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var examFlowService = scope.ServiceProvider.GetRequiredService<IExamFlowService>();

                    // DMs are not observed: recording a DM rename without a rescan would use the
                    // rename up before the next group message could act on it.
                    var identityService = scope.ServiceProvider.GetRequiredService<IUserIdentityService>();
                    var dmSender = await identityService.ResolveAsync(message.From.Id, cancellationToken);
                    var examContext = await examFlowService.GetActiveExamContextAsync(dmSender, cancellationToken);
                    if (examContext?.AwaitingOpenEndedAnswer == true)
                    {
                        var result = await examFlowService.HandleOpenEndedAnswerAsync(
                            examContext.GroupChatId,
                            dmSender,
                            message.Text,
                            cancellationToken);

                        LogOpenEndedExamAnswer(logger, message.From.ToLogInfo(), result.ExamComplete, result.Passed);

                        // Cancel welcome timeout and update response if exam completed
                        if (result.ExamComplete && result.GroupChatId.HasValue)
                        {
                            var welcomeRepo = scope.ServiceProvider.GetRequiredService<IWelcomeResponsesRepository>();
                            var jobScheduler = scope.ServiceProvider.GetRequiredService<IJobScheduler>();

                            var welcomeResponse = await welcomeRepo.GetByUserAndChatAsync(
                                message.From.Id, result.GroupChatId.Value, cancellationToken);

                            if (welcomeResponse?.TimeoutJobId != null)
                            {
                                await jobScheduler.CancelJobAsync(welcomeResponse.TimeoutJobId, cancellationToken);
                                await welcomeRepo.SetTimeoutJobIdAsync(welcomeResponse.Id, null, cancellationToken);
                            }

                            if (result.Passed == true && welcomeResponse != null)
                            {
                                await welcomeRepo.UpdateResponseAsync(
                                    welcomeResponse.Id, WelcomeResponseType.Accepted,
                                    dmSent: true, dmFallback: false, cancellationToken);
                            }
                            // SentToReview case: keep as Pending - admin will decide
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error processing open-ended exam answer from user {UserId}", message.From?.Id);
                }
            }

            pipelineMetrics.RecordMessageProcessed("new_message", "skipped",
                Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
            return; // Don't process private messages further
        }

        // Keep SDK Chat cache warm (used by NotificationHandler and other services)
        chatCache.UpdateChat(message.Chat);

        // Record message type metric for group messages
        var messageType = message switch
        {
            { Photo: not null } => "photo",
            { Video: not null } => "video",
            { Animation: not null } => "animation",
            { Document: not null } => "document",
            { Sticker: not null } => "sticker",
            _ when !string.IsNullOrEmpty(message.Text) => "text",
            _ => "other"
        };
        chatMetrics.RecordMessage(messageType);

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

            // Resolve scoped IBotChatService from scope (singleton can't inject scoped directly)
            using var migrationScope = scopeFactory.CreateScope();
            var chatService = migrationScope.ServiceProvider.GetRequiredService<IBotChatService>();
            await chatService.HandleChatMigrationAsync(oldChatId, newChatId, cancellationToken);
            pipelineMetrics.RecordMessageProcessed("new_message", "skipped",
                Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
            return; // Don't process migration message further
        }

        // Create a single DI scope for all group message processing operations
        using var messageScope = serviceProvider.CreateScope();

        // Record the sender's names first, so everything below (commands, detection, moderation)
        // sees one identity. A rename is rescanned inline, keeping a profile ban inside this
        // update's cleanup path. Messages without a sender (channel posts) are not observed.
        var observedAt = message.Date.ToUtcOffset();
        UserIdentity? sender = null;
        if (message.From is not null)
        {
            var identityService = messageScope.ServiceProvider.GetRequiredService<IUserIdentityService>();
            sender = await identityService.ObserveAsync(
                new ObservedUser(message.From.Id, message.From.FirstName, message.From.LastName, message.From.Username,
                    message.From.IsBot, ObservationSource.BotUpdate, observedAt),
                new ProfileChangeContext(ChatIdentity.From(message.Chat), message.MessageId),
                RenameRescan.Inline,
                cancellationToken);
        }

        // Handle service messages (join/leave, photo changes, title changes, etc.)
        // Check per-chat config to determine if each type should be deleted
        var configService = messageScope.ServiceProvider.GetRequiredService<IConfigService>();
        var deletionConfig = await configService.GetEffectiveServiceMessageDeletionAsync(message.Chat.Id, cancellationToken)
                             ?? ServiceMessageDeletionConfig.Default;

        if (ServiceMessageHelper.IsServiceMessage(message, deletionConfig, out var shouldDelete))
        {
            LogDetectedServiceMessage(logger, message.Type.ToString(), message.MessageId, message.Chat.ToLogDebug());

            // Store service message for UI consistency with Telegram Desktop
            var serviceMessageText = ServiceMessageHelper.GetServiceMessageText(message);
            if (serviceMessageText != null && sender is not null)
            {
                var serviceMessageRecord = new MessageRecord(
                    message.MessageId,
                    User: sender,
                    Chat: new Core.Models.ChatIdentity(message.Chat.Id, message.Chat.Title ?? message.Chat.Username),
                    DateTimeOffset.UtcNow,
                    serviceMessageText,
                    PhotoFileId: null,
                    PhotoFileSize: null,
                    Urls: null,
                    EditDate: null,
                    ContentHash: null,
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
                    ContentCheckSkipReason: ContentCheckSkipReason.ServiceMessage
                );

                var repository = messageScope.ServiceProvider.GetRequiredService<IMessageHistoryRepository>();
                await repository.InsertMessageAsync(serviceMessageRecord, cancellationToken);
            }

            // Skip deletion if the bot itself was removed from the group
            // Bot no longer has permissions to delete messages after being kicked
            if (message.LeftChatMember != null)
            {
                var userService = messageScope.ServiceProvider.GetRequiredService<IBotUserService>();
                var botInfo = await userService.GetMeAsync(cancellationToken);
                if (message.LeftChatMember.Id == botInfo.Id)
                {
                    LogSkippingBotRemovedDeletion(logger, message.Chat.ToLogInfo());
                    return; // Don't try to delete or process further
                }
            }

            if (shouldDelete)
            {
                try
                {
                    // Use BotMessageService for tracked deletion
                    var botMessageService = messageScope.ServiceProvider.GetRequiredService<IBotMessageService>();
                    await botMessageService.DeleteAndMarkMessageAsync(
                        message.Chat.Id,
                        message.MessageId,
                        deletionSource: "service_message",
                        cancellationToken);

                    LogDeletedServiceMessage(logger, message.Type.ToString(), message.Chat.ToLogInfo());
                }
                catch (Exception ex)
                {
                    logger.LogWarning(
                        ex,
                        "Failed to delete service message {MessageId} in {Chat}",
                        message.MessageId,
                        message.Chat.ToLogDebug());
                }
            }
            else
            {
                LogSkippingServiceMessageDeletion(logger, message.Type.ToString(), message.Chat.ToLogDebug());
            }

            pipelineMetrics.RecordMessageProcessed("new_message", "skipped",
                Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
            return; // Don't process service messages further
        }

        // Process messages from all group chats where bot is added
        try
        {
            var now = DateTimeOffset.UtcNow;

            // Update last_seen_at for this chat (also auto-adds chat if it doesn't exist)
            // Then refresh admin cache for newly discovered chats OR chats with empty admin cache
            var managedChatsRepository = messageScope.ServiceProvider.GetRequiredService<IManagedChatsRepository>();
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
                    var chatAdminsRepository = messageScope.ServiceProvider.GetRequiredService<IChatAdminsRepository>();
                    var adminCount = await chatAdminsRepository.GetAdminCountAsync(message.Chat.Id, cancellationToken);

                    if (adminCount == 0)
                    {
                        shouldRefreshAdmins = true;
                        refreshReason = "admin cache is empty";
                    }
                }

                if (shouldRefreshAdmins)
                {
                    LogRefreshingAdminCache(logger, message.Chat.ToLogInfo(), refreshReason);

                    try
                    {
                        var chatService = messageScope.ServiceProvider.GetRequiredService<IBotChatService>();
                        await chatService.RefreshChatAdminsAsync(ChatIdentity.From(message.Chat), cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to refresh admins for {Chat}", message.Chat.ToLogDebug());
                    }
                }
            }
            else
            {
                if (isNewChat)
                {
                    LogDiscoveredNewPrivateChat(logger, message.Chat.ToLogDebug());
                }
            }

            // Check if message is a bot command (execute first, then save to history)
            // Note: Errors are caught to ensure message history is always saved
            CommandResult? commandResult = null;
            if (commandRouter.IsCommand(message))
            {
                try
                {
                    commandResult = await commandRouter.RouteCommandAsync(message, cancellationToken);
                    if (commandResult != null)
                    {
                        // Send response if there is one (and it's not empty)
                        if (!string.IsNullOrWhiteSpace(commandResult.Message.Text))
                        {
                            // Use BotMessageService to save bot response to database
                            var botMessageService = messageScope.ServiceProvider.GetRequiredService<IBotMessageService>();
                            var responseMessage = await botMessageService.SendAndSaveMessageAsync(
                                message.Chat.Id,
                                commandResult.Message,
                                replyParameters: new ReplyParameters { MessageId = message.MessageId },
                                cancellationToken: cancellationToken);

                            // REFACTOR-2: Schedule auto-delete if requested using BackgroundJobScheduler
                            if (commandResult.DeleteResponseAfterSeconds.HasValue)
                            {
                                var jobScheduler = messageScope.ServiceProvider.GetRequiredService<Handlers.BackgroundJobScheduler>();
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
                        "Error executing command in {Chat}, message will still be saved to history",
                        message.Chat.ToLogDebug());
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
                        await adminMentionHandler.NotifyAdminsAsync(message, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex,
                        "Error handling @admin mention in {Chat}, message will still be saved to history",
                        message.Chat.ToLogDebug());
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

            var imageHandler = messageScope.ServiceProvider.GetRequiredService<Handlers.ImageProcessingHandler>();
            var imageResult = await imageHandler.ProcessImageAsync(
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

            var mediaHandler = messageScope.ServiceProvider.GetRequiredService<Handlers.MediaProcessingHandler>();
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

            // Calculate content hash for spam correlation
            var urlsJson = urls != null ? JsonSerializer.Serialize(urls) : "";
            var contentHash = HashUtilities.ComputeContentHash(text ?? "", urlsJson);

            // Check if chat icon is cached on disk
            var chatIconFileName = $"{Math.Abs(message.Chat.Id)}.jpg";
            var chatIconCachedPath = Path.Combine(appOptions.Value.DataPath, "media", "chat_icons", chatIconFileName);
            var chatIconPath = File.Exists(chatIconCachedPath) ? $"chat_icons/{chatIconFileName}" : null;

            // REFACTOR-1: Use ITranslationHandler for translation detection and processing
            var translationHandler = messageScope.ServiceProvider.GetRequiredService<Handlers.ITranslationHandler>();
            var translationForDetection = await translationHandler.GetTextForDetectionAsync(
                text,
                message.MessageId,
                message.Chat.Id,
                cancellationToken);
            var translation = translationForDetection.Translation;

            // Determine spam check skip reason (before saving message)
            // Check if user is trusted or admin to set appropriate skip reason
            var contentCheckSkipReason = ContentCheckSkipReason.NotSkipped; // Default: content checks will run

            if (message.From?.Id != null)
            {
                using var skipReasonScope = serviceProvider.CreateScope();
                var userRepository = skipReasonScope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
                var chatAdminsRepository = skipReasonScope.ServiceProvider.GetRequiredService<IChatAdminsRepository>();

                // Check admin status first (higher priority)
                bool isUserAdmin = await chatAdminsRepository.IsAdminAsync(message.Chat.Id, message.From.Id, cancellationToken);
                if (isUserAdmin)
                {
                    contentCheckSkipReason = ContentCheckSkipReason.UserAdmin;
                }
                else
                {
                    // REFACTOR-5: Check trust status (source of truth: telegram_users.is_trusted)
                    bool isUserTrusted = await userRepository.IsTrustedAsync(
                        message.From.Id,
                        cancellationToken);

                    if (isUserTrusted)
                    {
                        contentCheckSkipReason = ContentCheckSkipReason.UserTrusted;
                    }
                }
            }

            // Every step from here on needs the sender.
            if (sender is null)
                throw new InvalidOperationException($"Group message {message.MessageId} has no sender");

            // User photo will be fetched asynchronously after message save (non-blocking)
            var messageRecord = new MessageRecord(
                message.MessageId,
                User: sender,
                Chat: new Core.Models.ChatIdentity(message.Chat.Id, message.Chat.Title ?? message.Chat.Username),
                now,
                text,
                photoFileId,
                photoFileSize,
                urlsJson != "" ? urlsJson : null,
                EditDate: message.EditDate.HasValue ? new DateTimeOffset(message.EditDate.Value, TimeSpan.Zero) : null,
                ContentHash: contentHash,
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
                ContentCheckSkipReason: contentCheckSkipReason
            );

            // Save message to database using the scoped repository
            var repository = messageScope.ServiceProvider.GetRequiredService<IMessageHistoryRepository>();
            await repository.InsertMessageAsync(messageRecord, cancellationToken);

            // Schedule file scan AFTER message is persisted (FK constraint requires message to exist)
            var fileScanHandler = messageScope.ServiceProvider.GetRequiredService<Handlers.FileScanningHandler>();
            await fileScanHandler.ProcessFileScanningAsync(
                message,
                message.Chat.Id,
                message.From!.Id,
                cancellationToken);

            // CRITICAL: Check if user was banned while this message was being processed
            // Handles race condition where multiple messages arrive simultaneously:
            // - Message 1 triggers spam detection → user banned
            // - Message 2 (this one) was being processed in parallel → saved to DB
            // - Without this check, Message 2 stays in chat forever
            // With this check, Message 2 gets deleted immediately
            // REFACTOR-5: Use is_banned column as source of truth (global ban status)
            var userRepoForBanCheck = messageScope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
            bool isUserBanned = await userRepoForBanCheck.IsBannedAsync(
                message.From!.Id,
                cancellationToken);

            if (isUserBanned)
            {
                logger.LogWarning(
                    "{User} is already banned in {Chat}, deleting message {MessageId} immediately (multi-message spam cleanup)",
                    message.From.ToLogDebug(),
                    message.Chat.ToLogDebug(),
                    message.MessageId);

                var moderationService = messageScope.ServiceProvider.GetRequiredService<IBotModerationService>();
                await moderationService.DeleteMessageAsync(
                    new DeleteMessageIntent
                    {
                        MessageId = message.MessageId,
                        Chat = ChatIdentity.From(message.Chat),
                        User = sender,
                        Executor = Core.Models.Actor.AutoDetection,
                        Reason = "User banned during message processing (multi-message spam campaign)"
                    },
                    cancellationToken);

                // Lazy sync: Apply Telegram-level ban to this chat
                // (User may have joined this chat before it was added to the bot's managed chats)
                await moderationService.SyncBanToChatAsync(
                    new SyncBanIntent
                    {
                        User = sender,
                        Chat = ChatIdentity.From(message.Chat),
                        Executor = Core.Models.Actor.AutoDetection,
                        Reason = "Lazy ban sync: User was globally banned before this chat was added",
                        TriggeredByMessageId = message.MessageId
                    },
                    cancellationToken);

                // Don't process this message further (no spam detection, no user photo fetch, etc.)
                return;
            }

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
            if (urls != null && urls.Any() && text != null)
            {
                var urlScrapingService = messageScope.ServiceProvider.GetRequiredService<TelegramGroupsAdmin.ContentDetection.Services.IUrlContentScrapingService>();
                var enrichedText = await urlScrapingService.EnrichMessageWithUrlPreviewsAsync(text, cancellationToken);

                if (enrichedText != text) // Content was enriched
                {
                    await repository.UpdateMessageTextAsync(message.MessageId, message.Chat.Id, enrichedText, cancellationToken);
                    text = enrichedText; // Use enriched text for content detection later

                    logger.LogDebug(
                        "Enriched message {MessageId} with URL previews ({UrlCount} URLs)",
                        message.MessageId,
                        urls.Count);
                }
            }

            await messageScope.ServiceProvider.GetRequiredService<ITelegramUserRepository>()
                .MarkActiveAsync(message.From!.Id, observedAt, cancellationToken);

            // Profile scan for users we have never scanned. Users who arrive
            // without a join event (for example, accounts commenting on channel
            // posts in a linked discussion group) are not covered by the join
            // trigger, and have no prior names for a rename to be detected against.
            // The observation at the start of this method creates the user row for
            // the scan-result and report foreign keys, unless recording it failed
            // (which ObserveAsync logs). The gate owns the whole
            // eligibility decision, including whether this user was scanned before.
            var profileScanBanned = false;

            try
            {
                var profileScanGate = messageScope.ServiceProvider.GetRequiredService<IProfileScanGate>();
                var firstMessageScan = await profileScanGate.ScanIfEligibleAsync(
                    sender,
                    ChatIdentity.From(message.Chat),
                    ProfileScanTrigger.FirstMessage,
                    ct: cancellationToken);

                if (firstMessageScan?.Outcome == ProfileScanOutcome.Banned)
                {
                    profileScanBanned = true;
                    LogFirstMessageScanBanned(logger, message.From.ToLogDebug());
                }
            }
            catch (Exception ex)
            {
                // A failed scan must never cost us the message.
                LogProfileScanFailed(logger, ex, message.From.ToLogDebug());
            }

            // Raise event for real-time UI updates
            OnNewMessage?.Invoke(messageRecord);

            LogCachedMessage(logger, message.MessageId, message.From.ToLogDebug(), message.Chat.ToLogDebug(), photoFileId != null, text != null);

            // Delete command message if requested (AFTER saving to database for FK integrity)
            if (commandResult?.DeleteCommandMessage == true)
            {
                try
                {
                    // Use BotMessageService for tracked deletion
                    var botMessageService = messageScope.ServiceProvider.GetRequiredService<IBotMessageService>();
                    await botMessageService.DeleteAndMarkMessageAsync(
                        message.Chat.Id,
                        message.MessageId,
                        deletionSource: "command_cleanup",
                        cancellationToken);

                    LogDeletedCommandMessage(logger, message.MessageId, message.Chat.ToLogDebug());
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to delete command message {MessageId} in {Chat}", message.MessageId, message.Chat.ToLogDebug());
                }
            }

            // REFACTOR-2: Fetch user profile photo via Quartz.NET using BackgroundJobScheduler
            if (message.From?.Id != null)
            {
                var jobScheduler = messageScope.ServiceProvider.GetRequiredService<Handlers.BackgroundJobScheduler>();
                await jobScheduler.ScheduleUserPhotoFetchAsync(message.MessageId, message.From.Id, cancellationToken);
            }

            // REFACTOR-2: Automatic content detection using ContentDetectionOrchestrator
            // Skip content detection for command messages (already processed by CommandRouter)
            // Skip content detection for inactive chats (bot not admin - can't take moderation actions)
            // Run content detection if message has text OR images (image-only spam detection)
            if (!profileScanBanned
                && commandResult == null
                && (!string.IsNullOrWhiteSpace(text) || photoLocalPath != null))
            {
                // Check if chat is active (bot has admin permissions) before running detection
                using var chatCheckScope = serviceProvider.CreateScope();
                var chatRepo = chatCheckScope.ServiceProvider.GetRequiredService<IManagedChatsRepository>();
                var managedChat = await chatRepo.GetByChatIdAsync(message.Chat.Id, cancellationToken);

                if (managedChat == null || !managedChat.IsActive)
                {
                    // Chat is inactive (bot not admin) - skip content detection
                    // Message is already saved, just don't process for spam
                    LogSkippingContentDetectionInactiveChat(logger, message.Chat.ToLogDebug());
                }
                else
                {
                    using var detectionScope = serviceProvider.CreateScope();
                    var contentOrchestrator = detectionScope.ServiceProvider.GetRequiredService<Handlers.ContentDetectionOrchestrator>();

                    // Use translated text if available (avoids double translation in ContentDetectionEngine)
                    await contentOrchestrator.RunDetectionAsync(
                        message,
                        translationForDetection.TextForDetection,
                        photoLocalPath,
                        editVersion: 0,
                        cancellationToken);
                }
            }

            pipelineMetrics.RecordMessageProcessed("new_message", "processed",
                Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
        }
        catch (Exception ex)
        {
            pipelineMetrics.RecordMessageProcessed("new_message", "error",
                Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
            logger.LogError(ex,
                "Error caching message {MessageId} from {User} in {Chat}",
                message.MessageId,
                message.From.ToLogDebug(),
                message.Chat.ToLogDebug());
        }
    }

    /// <summary>
    /// REFACTOR-2: Handle edited messages using MessageEditProcessor
    /// </summary>
    public async Task HandleEditedMessageAsync(Message editedMessage, CancellationToken cancellationToken = default)
    {
        using var activity = TelemetryConstants.MessageProcessing.StartActivity("message_processing.handle_edited_message");
        activity?.SetTag("message.chat_id", editedMessage.Chat.Id);
        activity?.SetTag("message.message_id", editedMessage.MessageId);
        activity?.SetTag("message.chat_type", editedMessage.Chat.Type.ToString());

        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            using var scope = scopeFactory.CreateScope();
            var editProcessor = scope.ServiceProvider.GetRequiredService<Handlers.MessageEditProcessor>();

            var editRecord = await editProcessor.ProcessEditAsync(editedMessage, scope, cancellationToken);

            // Raise event for real-time UI updates (if edit actually occurred)
            if (editRecord != null)
            {
                OnMessageEdited?.Invoke(editRecord);
            }

            pipelineMetrics.RecordMessageProcessed("edit", "processed",
                Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
        }
        catch (Exception ex)
        {
            pipelineMetrics.RecordMessageProcessed("edit", "error",
                Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
            logger.LogError(ex,
                "Error handling edit for message {MessageId}",
                editedMessage.MessageId);
        }
    }

    // REFACTOR-2: Extracted methods to specialized handlers
    //   Phase 1: ImageProcessingHandler, BackgroundJobScheduler
    //   Phase 2: SpamDetectionOrchestrator, LanguageWarningHandler
    //   Phase 3: MessageEditProcessor
    //   Phase 4: FileScanningHandler (replaces ScheduleFileScanJobAsync dead code)
}
