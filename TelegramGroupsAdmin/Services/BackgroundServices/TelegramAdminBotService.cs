using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Models;
using TelegramGroupsAdmin.Repositories;
using TelegramGroupsAdmin.Services.BotCommands;
using TelegramGroupsAdmin.Services.Telegram;

namespace TelegramGroupsAdmin.Services.BackgroundServices;

public partial class TelegramAdminBotService(
    TelegramBotClientFactory botFactory,
    MessageHistoryRepository repository,
    IOptions<TelegramOptions> options,
    IOptions<MessageHistoryOptions> historyOptions,
    CommandRouter commandRouter,
    IServiceProvider serviceProvider,
    ILogger<TelegramAdminBotService> logger)
    : BackgroundService, IMessageHistoryService
{
    private readonly TelegramOptions _options = options.Value;
    private readonly MessageHistoryOptions _historyOptions = historyOptions.Value;
    private ITelegramBotClient? _botClient;
    private readonly ConcurrentDictionary<long, ChatHealthStatus> _healthCache = new();

    // Events for real-time UI updates
    public event Action<MessageRecord>? OnNewMessage;
    public event Action<MessageEditRecord>? OnMessageEdited;
    public event Action<ChatHealthStatus>? OnHealthUpdate;

    /// <summary>
    /// Get the bot client instance (available after service starts)
    /// </summary>
    public ITelegramBotClient? BotClient => _botClient;

    /// <summary>
    /// Get cached health status for a chat (null if not yet checked)
    /// </summary>
    public ChatHealthStatus? GetCachedHealth(long chatId)
        => _healthCache.TryGetValue(chatId, out var health) ? health : null;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Check if Telegram admin bot is enabled
        if (!_historyOptions.Enabled)
        {
            logger.LogInformation("Telegram admin bot is disabled (MESSAGEHISTORY__ENABLED=false). Service will not start.");
            return;
        }

        _botClient = botFactory.GetOrCreate(_options.BotToken);
        var botClient = _botClient;

        // Register bot commands in Telegram UI
        await RegisterBotCommandsAsync(botClient, stoppingToken);

        // Cache admin lists for all managed chats
        await RefreshAllChatAdminsAsync(botClient, stoppingToken);

        // Perform initial health check for all chats
        await RefreshAllHealthAsync();

        // Start periodic health check timer (runs every 1 minute)
        _ = Task.Run(async () =>
        {
            var healthTimer = new PeriodicTimer(TimeSpan.FromMinutes(1));
            try
            {
                while (await healthTimer.WaitForNextTickAsync(stoppingToken))
                {
                    await RefreshAllHealthAsync();
                }
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("Health check timer cancelled");
            }
            finally
            {
                healthTimer.Dispose();
            }
        }, stoppingToken);

        logger.LogInformation("Telegram admin bot started listening for messages in all chats");

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = [UpdateType.Message, UpdateType.EditedMessage, UpdateType.MyChatMember],
            DropPendingUpdates = true
        };

        try
        {
            await botClient.ReceiveAsync(
                updateHandler: HandleUpdateAsync,
                errorHandler: HandleErrorAsync,
                receiverOptions: receiverOptions,
                cancellationToken: stoppingToken);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("HistoryBot stopped");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "HistoryBot encountered fatal error");
        }
    }

    private async Task HandleUpdateAsync(
        ITelegramBotClient botClient,
        Update update,
        CancellationToken cancellationToken)
    {
        // Handle bot's chat member status changes (added/removed from chats)
        if (update.MyChatMember is { } myChatMember)
        {
            await HandleMyChatMemberUpdateAsync(myChatMember);
            return;
        }

        // Handle new messages
        if (update.Message is { } message)
        {
            await HandleNewMessageAsync(botClient, message);
            return;
        }

        // Handle edited messages
        if (update.EditedMessage is { } editedMessage)
        {
            await HandleEditedMessageAsync(editedMessage);
            return;
        }
    }

    private async Task HandleNewMessageAsync(ITelegramBotClient botClient, Message message)
    {
        // Process messages from all chats where bot is added
        try
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // Update last_seen_at for this chat (also auto-adds chat if it doesn't exist)
            // Then refresh admin cache for newly discovered chats
            using (var scope = serviceProvider.CreateScope())
            {
                var managedChatsRepository = scope.ServiceProvider.GetRequiredService<IManagedChatsRepository>();
                var existingChat = await managedChatsRepository.GetByChatIdAsync(message.Chat.Id);
                var isNewChat = existingChat == null;

                await managedChatsRepository.UpdateLastSeenAsync(message.Chat.Id, now);

                // If this is a newly discovered chat, refresh its admin cache
                if (isNewChat)
                {
                    logger.LogInformation("Discovered new chat {ChatId}, refreshing admin cache", message.Chat.Id);
                    try
                    {
                        await RefreshChatAdminsAsync(botClient, message.Chat.Id, default);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to refresh admins for newly discovered chat {ChatId}", message.Chat.Id);
                    }
                }
            }

            // Check if message is a bot command (execute first, then save to history)
            // Note: Errors are caught to ensure message history is always saved
            CommandResult? commandResult = null;
            if (commandRouter.IsCommand(message))
            {
                try
                {
                    commandResult = await commandRouter.RouteCommandAsync(botClient, message);
                    if (commandResult != null)
                    {
                        // Send response if there is one
                        if (commandResult.Response != null)
                        {
                            await botClient.SendMessage(
                                chatId: message.Chat.Id,
                                text: commandResult.Response,
                                parseMode: ParseMode.Markdown,
                                replyParameters: new ReplyParameters { MessageId = message.MessageId });
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
                    using (var scope = serviceProvider.CreateScope())
                    {
                        var adminMentionHandler = scope.ServiceProvider.GetRequiredService<AdminMentionHandler>();
                        if (adminMentionHandler.ContainsAdminMention(text))
                        {
                            await adminMentionHandler.NotifyAdminsAsync(botClient, message);
                        }
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
            var urls = text != null ? ExtractUrls(text) : null;

            // Get photo file ID if present and download image
            string? photoFileId = null;
            int? photoFileSize = null;
            string? photoLocalPath = null;
            string? photoThumbnailPath = null;

            if (message.Photo is { Length: > 0 } photos)
            {
                var largestPhoto = photos.OrderByDescending(p => p.FileSize).First();
                photoFileId = largestPhoto.FileId;
                photoFileSize = largestPhoto.FileSize.HasValue ? (int)largestPhoto.FileSize.Value : null;

                // Download and process image
                (photoLocalPath, photoThumbnailPath) = await DownloadAndProcessImageAsync(
                    botClient,
                    photoFileId,
                    message.Chat.Id,
                    message.MessageId);
            }

            // Calculate content hash for spam correlation
            var urlsJson = urls != null ? JsonSerializer.Serialize(urls) : "";
            var contentHash = ComputeContentHash(text ?? "", urlsJson);

            var messageRecord = new MessageRecord(
                message.MessageId,
                message.From!.Id,
                message.From.Username ?? message.From.FirstName,
                message.Chat.Id,
                now,
                text,
                photoFileId,
                photoFileSize,
                urlsJson != "" ? urlsJson : null,
                EditDate: message.EditDate.HasValue ? new DateTimeOffset(message.EditDate.Value, TimeSpan.Zero).ToUnixTimeSeconds() : null,
                ContentHash: contentHash,
                ChatName: message.Chat.Title ?? message.Chat.Username,
                PhotoLocalPath: photoLocalPath,
                PhotoThumbnailPath: photoThumbnailPath,
                DeletedAt: null,
                DeletionSource: null
            );

            await repository.InsertMessageAsync(messageRecord);

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
                    await botClient.DeleteMessage(
                        chatId: message.Chat.Id,
                        messageId: message.MessageId);

                    logger.LogDebug("Deleted command message {MessageId} in chat {ChatId}", message.MessageId, message.Chat.Id);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to delete command message {MessageId} in chat {ChatId}", message.MessageId, message.Chat.Id);
                }
            }

            // Phase 2.6: Automatic spam detection with detection result storage
            // Skip spam detection for command messages (already processed by CommandRouter)
            if (commandResult == null && !string.IsNullOrWhiteSpace(text))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var scope = serviceProvider.CreateScope();
                        var orchestrator = scope.ServiceProvider.GetRequiredService<ISpamCheckOrchestrator>();
                        var detectionResultsRepo = scope.ServiceProvider.GetRequiredService<IDetectionResultsRepository>();
                        var spamDetectorFactory = scope.ServiceProvider.GetRequiredService<TelegramGroupsAdmin.SpamDetection.Services.ISpamDetectorFactory>();

                        var request = new TelegramGroupsAdmin.SpamDetection.Models.SpamCheckRequest
                        {
                            Message = text,
                            UserId = message.From?.Id.ToString() ?? "",
                            UserName = message.From?.Username,
                            ChatId = message.Chat.Id.ToString()
                            // TODO: Add ImageData stream for image spam detection (Phase 2.7)
                        };

                        var result = await orchestrator.CheckAsync(request);

                        // Store detection result (spam or ham) for analytics and training
                        // Only store if spam detection actually ran (not skipped for trusted/admin users)
                        if (!result.SpamCheckSkipped && result.SpamResult != null)
                        {
                            var detectionResult = new Models.DetectionResultRecord
                            {
                                MessageId = message.MessageId,
                                DetectedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                                DetectionSource = "auto",
                                DetectionMethod = result.SpamResult.CheckResults.Count > 0
                                    ? string.Join(", ", result.SpamResult.CheckResults.Select(c => c.CheckName))
                                    : "Unknown",
                                IsSpam = result.SpamResult.IsSpam,
                                Confidence = result.SpamResult.MaxConfidence,
                                Reason = result.SpamResult.PrimaryReason,
                                AddedBy = null, // Auto-detection
                                UsedForTraining = DetermineIfTrainingWorthy(result.SpamResult),
                                NetConfidence = result.SpamResult.NetConfidence,
                                CheckResultsJson = spamDetectorFactory.GetType()
                                    .GetMethod("SerializeCheckResults", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                                    ?.Invoke(null, new object[] { result.SpamResult.CheckResults }) as string,
                                EditVersion = 0
                            };

                            await detectionResultsRepo.InsertAsync(detectionResult);

                            logger.LogInformation(
                                "Stored detection result for message {MessageId}: {IsSpam} (net: {NetConfidence}, training: {UsedForTraining})",
                                message.MessageId,
                                result.SpamResult.IsSpam ? "spam" : "ham",
                                result.SpamResult.NetConfidence,
                                detectionResult.UsedForTraining);

                            // Phase 2.7: Handle spam actions based on net confidence
                            await HandleSpamDetectionActionsAsync(
                                scope.ServiceProvider,
                                message,
                                result.SpamResult,
                                detectionResult);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to run spam detection for message {MessageId}", message.MessageId);
                    }
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

    private Task HandleErrorAsync(
        ITelegramBotClient botClient,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(exception, "HistoryBot polling error");
        return Task.CompletedTask;
    }

    private static List<string>? ExtractUrls(string text)
    {
        var matches = UrlRegex().Matches(text);
        return matches.Count > 0
            ? matches.Select(m => m.Value).ToList()
            : null;
    }

    [GeneratedRegex(@"https?://[^\s\]\)\>]+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex UrlRegex();

    private async Task<(string? fullPath, string? thumbPath)> DownloadAndProcessImageAsync(
        ITelegramBotClient botClient,
        string photoFileId,
        long chatId,
        long messageId)
    {
        try
        {
            // Create directory structure: {ImageStoragePath}/full/{chat_id}/ and thumbs/{chat_id}/
            var basePath = _historyOptions.ImageStoragePath;
            var fullDir = Path.Combine(basePath, "full", chatId.ToString());
            var thumbDir = Path.Combine(basePath, "thumbs", chatId.ToString());

            Directory.CreateDirectory(fullDir);
            Directory.CreateDirectory(thumbDir);

            var fileName = $"{messageId}.jpg";
            var fullPath = Path.Combine(fullDir, fileName);
            var thumbPath = Path.Combine(thumbDir, fileName);

            // Download file from Telegram
            var file = await botClient.GetFile(photoFileId);
            if (file.FilePath == null)
            {
                logger.LogWarning("Unable to get file path for photo {FileId}", photoFileId);
                return (null, null);
            }

            // Download to temp file first
            var tempPath = Path.GetTempFileName();
            try
            {
                await using (var fileStream = System.IO.File.Create(tempPath))
                {
                    await botClient.DownloadFile(file.FilePath, fileStream);
                }

                // Copy to full image location
                System.IO.File.Copy(tempPath, fullPath, overwrite: true);

                // Generate thumbnail using ImageSharp
                using (var image = await Image.LoadAsync(tempPath))
                {
                    var thumbnailSize = _historyOptions.ThumbnailSize;
                    image.Mutate(x => x.Resize(new ResizeOptions
                    {
                        Size = new Size(thumbnailSize, thumbnailSize),
                        Mode = ResizeMode.Max // Maintain aspect ratio
                    }));

                    await image.SaveAsJpegAsync(thumbPath);
                }

                logger.LogDebug(
                    "Downloaded and processed image for message {MessageId} in chat {ChatId}",
                    messageId, chatId);

                // Return relative paths for storage in database
                return ($"full/{chatId}/{fileName}", $"thumbs/{chatId}/{fileName}");
            }
            finally
            {
                // Clean up temp file
                if (System.IO.File.Exists(tempPath))
                    System.IO.File.Delete(tempPath);
            }
        }
        catch (IOException ioEx)
        {
            // Disk full or permissions error - fail open (don't block message, just skip image)
            logger.LogWarning(ioEx,
                "Filesystem error downloading image for message {MessageId} in chat {ChatId}. Message will be stored without image.",
                messageId, chatId);
            return (null, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Error downloading/processing image for message {MessageId} in chat {ChatId}",
                messageId, chatId);
            return (null, null);
        }
    }

    private async Task HandleEditedMessageAsync(Message editedMessage)
    {
        try
        {
            // Get the old message from the database
            var oldMessage = await repository.GetMessageAsync(editedMessage.MessageId);
            if (oldMessage == null)
            {
                logger.LogWarning(
                    "Received edit for unknown message {MessageId}",
                    editedMessage.MessageId);
                return;
            }

            var oldText = oldMessage.MessageText;
            var newText = editedMessage.Text ?? editedMessage.Caption;

            // Skip if text hasn't actually changed
            if (oldText == newText)
            {
                logger.LogDebug(
                    "Edit event for message {MessageId} but text unchanged, skipping",
                    editedMessage.MessageId);
                return;
            }

            var editDate = editedMessage.EditDate.HasValue
                ? new DateTimeOffset(editedMessage.EditDate.Value, TimeSpan.Zero).ToUnixTimeSeconds()
                : DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // Extract URLs and calculate content hashes
            var oldUrls = oldText != null ? ExtractUrls(oldText) : null;
            var newUrls = newText != null ? ExtractUrls(newText) : null;

            var oldContentHash = ComputeContentHash(oldText ?? "", oldUrls != null ? JsonSerializer.Serialize(oldUrls) : "");
            var newContentHash = ComputeContentHash(newText ?? "", newUrls != null ? JsonSerializer.Serialize(newUrls) : "");

            // Create edit record
            var editRecord = new MessageEditRecord(
                Id: 0, // Will be set by INSERT
                MessageId: editedMessage.MessageId,
                EditDate: editDate,
                OldText: oldText,
                NewText: newText,
                OldContentHash: oldContentHash,
                NewContentHash: newContentHash
            );

            await repository.InsertMessageEditAsync(editRecord);

            // Update the message in the messages table with new text and edit date
            var updatedMessage = oldMessage with
            {
                MessageText = newText,
                EditDate = editDate,
                Urls = newUrls != null ? JsonSerializer.Serialize(newUrls) : null,
                ContentHash = newContentHash
            };
            await repository.UpdateMessageAsync(updatedMessage);

            // Raise event for real-time UI updates
            OnMessageEdited?.Invoke(editRecord);

            logger.LogInformation(
                "Recorded edit for message {MessageId} in chat {ChatId}",
                editedMessage.MessageId,
                editedMessage.Chat.Id);

            // Phase 2.7: Re-scan edited message for spam (detect "post innocent, edit to spam" tactic)
            if (!string.IsNullOrWhiteSpace(newText))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var scope = serviceProvider.CreateScope();
                        var orchestrator = scope.ServiceProvider.GetRequiredService<ISpamCheckOrchestrator>();
                        var detectionResultsRepo = scope.ServiceProvider.GetRequiredService<IDetectionResultsRepository>();
                        var spamDetectorFactory = scope.ServiceProvider.GetRequiredService<TelegramGroupsAdmin.SpamDetection.Services.ISpamDetectorFactory>();

                        var request = new TelegramGroupsAdmin.SpamDetection.Models.SpamCheckRequest
                        {
                            Message = newText,
                            UserId = editedMessage.From?.Id.ToString() ?? "",
                            UserName = editedMessage.From?.Username,
                            ChatId = editedMessage.Chat.Id.ToString()
                        };

                        var result = await orchestrator.CheckAsync(request);

                        // Store detection result with incremented edit_version
                        if (!result.SpamCheckSkipped && result.SpamResult != null)
                        {
                            // Get the latest edit_version for this message
                            var existingResults = await detectionResultsRepo.GetByMessageIdAsync(editedMessage.MessageId);
                            var maxEditVersion = existingResults.Any()
                                ? existingResults.Max(r => r.EditVersion)
                                : 0;

                            var detectionResult = new Models.DetectionResultRecord
                            {
                                MessageId = editedMessage.MessageId,
                                DetectedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                                DetectionSource = "auto",
                                DetectionMethod = result.SpamResult.CheckResults.Count > 0
                                    ? string.Join(", ", result.SpamResult.CheckResults.Select(c => c.CheckName))
                                    : "Unknown",
                                IsSpam = result.SpamResult.IsSpam,
                                Confidence = result.SpamResult.MaxConfidence,
                                Reason = $"[Edit #{maxEditVersion + 1}] {result.SpamResult.PrimaryReason}",
                                AddedBy = null, // Auto-detection
                                UsedForTraining = DetermineIfTrainingWorthy(result.SpamResult),
                                NetConfidence = result.SpamResult.NetConfidence,
                                CheckResultsJson = spamDetectorFactory.GetType()
                                    .GetMethod("SerializeCheckResults", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                                    ?.Invoke(null, new object[] { result.SpamResult.CheckResults }) as string,
                                EditVersion = maxEditVersion + 1
                            };

                            await detectionResultsRepo.InsertAsync(detectionResult);

                            logger.LogInformation(
                                "Stored detection result for edited message {MessageId} (edit #{EditVersion}): {IsSpam} (net: {NetConfidence}, training: {UsedForTraining})",
                                editedMessage.MessageId,
                                detectionResult.EditVersion,
                                result.SpamResult.IsSpam ? "spam" : "ham",
                                result.SpamResult.NetConfidence,
                                detectionResult.UsedForTraining);

                            // Take action if edited content is spam
                            await HandleSpamDetectionActionsAsync(
                                scope.ServiceProvider,
                                editedMessage,
                                result.SpamResult,
                                detectionResult);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to re-scan edited message {MessageId} for spam", editedMessage.MessageId);
                    }
                }, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Error handling edit for message {MessageId}",
                editedMessage.MessageId);
        }
    }

    private async Task HandleMyChatMemberUpdateAsync(ChatMemberUpdated myChatMember)
    {
        try
        {
            var chat = myChatMember.Chat;
            var oldStatus = myChatMember.OldChatMember.Status;
            var newStatus = myChatMember.NewChatMember.Status;
            var isAdmin = newStatus == ChatMemberStatus.Administrator;
            var isActive = newStatus is ChatMemberStatus.Member or ChatMemberStatus.Administrator;

            // Map Telegram ChatType to our ManagedChatType enum
            var chatType = chat.Type switch
            {
                ChatType.Private => ManagedChatType.Private,
                ChatType.Group => ManagedChatType.Group,
                ChatType.Supergroup => ManagedChatType.Supergroup,
                ChatType.Channel => ManagedChatType.Channel,
                _ => ManagedChatType.Group
            };

            // Map Telegram ChatMemberStatus to our BotChatStatus enum
            var botStatus = newStatus switch
            {
                ChatMemberStatus.Member => BotChatStatus.Member,
                ChatMemberStatus.Administrator => BotChatStatus.Administrator,
                ChatMemberStatus.Left => BotChatStatus.Left,
                ChatMemberStatus.Kicked => BotChatStatus.Kicked,
                _ => BotChatStatus.Member
            };

            var chatRecord = new ManagedChatRecord(
                ChatId: chat.Id,
                ChatName: chat.Title ?? chat.Username ?? $"Chat {chat.Id}",
                ChatType: chatType,
                BotStatus: botStatus,
                IsAdmin: isAdmin,
                AddedAt: DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                IsActive: isActive,
                LastSeenAt: DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                SettingsJson: null
            );

            using (var scope = serviceProvider.CreateScope())
            {
                var managedChatsRepository = scope.ServiceProvider.GetRequiredService<IManagedChatsRepository>();
                await managedChatsRepository.UpsertAsync(chatRecord);
            }

            // If this is about ANOTHER user (not the bot), update admin cache
            var botUser = myChatMember.From;
            var affectedUser = myChatMember.NewChatMember.User;

            if (affectedUser.IsBot == false) // Admin change for a real user
            {
                var wasAdmin = oldStatus is ChatMemberStatus.Creator or ChatMemberStatus.Administrator;
                var isNowAdmin = newStatus is ChatMemberStatus.Creator or ChatMemberStatus.Administrator;

                if (wasAdmin != isNowAdmin)
                {
                    using var scope = serviceProvider.CreateScope();
                    var chatAdminsRepository = scope.ServiceProvider.GetRequiredService<IChatAdminsRepository>();

                    if (isNowAdmin)
                    {
                        // User promoted to admin
                        var isCreator = newStatus == ChatMemberStatus.Creator;
                        await chatAdminsRepository.UpsertAsync(chat.Id, affectedUser.Id, isCreator);
                        logger.LogInformation(
                            "✅ User {UserId} (@{Username}) promoted to {Role} in chat {ChatId}",
                            affectedUser.Id,
                            affectedUser.Username ?? "unknown",
                            isCreator ? "creator" : "admin",
                            chat.Id);
                    }
                    else
                    {
                        // User demoted from admin
                        await chatAdminsRepository.DeactivateAsync(chat.Id, affectedUser.Id);
                        logger.LogInformation(
                            "❌ User {UserId} (@{Username}) demoted from admin in chat {ChatId}",
                            affectedUser.Id,
                            affectedUser.Username ?? "unknown",
                            chat.Id);
                    }
                }
            }

            if (isActive)
            {
                logger.LogInformation(
                    "✅ Bot added to {ChatType} {ChatId} ({ChatName}) as {Status}",
                    chat.Type,
                    chat.Id,
                    chat.Title ?? chat.Username ?? "Unknown",
                    newStatus);
            }
            else
            {
                logger.LogWarning(
                    "❌ Bot removed from {ChatId} ({ChatName}) - status: {Status}",
                    chat.Id,
                    chat.Title ?? chat.Username ?? "Unknown",
                    newStatus);
            }

            // Trigger immediate health check when bot status changes
            await RefreshHealthForChatAsync(chat.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Error handling MyChatMember update for chat {ChatId}",
                myChatMember.Chat.Id);
        }
    }

    private static string ComputeContentHash(string messageText, string urls)
    {
        var normalized = $"{messageText.ToLowerInvariant().Trim()}{urls.ToLowerInvariant().Trim()}";
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hashBytes);
    }

    private async Task RegisterBotCommandsAsync(ITelegramBotClient botClient, CancellationToken cancellationToken)
    {
        try
        {
            // Register commands with different scopes based on permission levels

            // Default scope - commands for all users (ReadOnly level 0)
            var defaultCommands = commandRouter.GetAvailableCommands(permissionLevel: 0)
                .Select(cmd => new BotCommand
                {
                    Command = cmd.Name,
                    Description = cmd.Description
                })
                .ToArray();

            await botClient.SetMyCommands(
                defaultCommands,
                scope: new BotCommandScopeDefault(),
                cancellationToken: cancellationToken);

            // Admin scope - commands for group admins (Admin level 1+)
            var adminCommands = commandRouter.GetAvailableCommands(permissionLevel: 1)
                .Select(cmd => new BotCommand
                {
                    Command = cmd.Name,
                    Description = cmd.Description
                })
                .ToArray();

            await botClient.SetMyCommands(
                adminCommands,
                scope: new BotCommandScopeAllChatAdministrators(),
                cancellationToken: cancellationToken);

            logger.LogInformation(
                "Registered bot commands - {DefaultCount} default, {AdminCount} admin",
                defaultCommands.Length,
                adminCommands.Length);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to register bot commands with Telegram");
        }
    }

    /// <summary>
    /// Refresh admin cache for all active managed chats on startup
    /// </summary>
    private async Task RefreshAllChatAdminsAsync(ITelegramBotClient botClient, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var managedChatsRepository = scope.ServiceProvider.GetRequiredService<IManagedChatsRepository>();
            var managedChats = await managedChatsRepository.GetActiveChatsAsync();

            logger.LogInformation("Refreshing admin cache for {Count} managed chats", managedChats.Count);

            var refreshedCount = 0;
            foreach (var chat in managedChats)
            {
                try
                {
                    await RefreshChatAdminsAsync(botClient, chat.ChatId, cancellationToken);
                    refreshedCount++;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to refresh admin cache for chat {ChatId}", chat.ChatId);
                }
            }

            logger.LogInformation("✅ Admin cache refreshed for {Count}/{Total} chats", refreshedCount, managedChats.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to refresh admin cache on startup");
        }
    }

    /// <summary>
    /// Refresh admin list for a specific chat
    /// </summary>
    private async Task RefreshChatAdminsAsync(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
    {
        try
        {
            // Get all administrators from Telegram
            var admins = await botClient.GetChatAdministrators(chatId, cancellationToken);

            using var scope = serviceProvider.CreateScope();
            var chatAdminsRepository = scope.ServiceProvider.GetRequiredService<IChatAdminsRepository>();

            var adminNames = new List<string>();
            foreach (var admin in admins)
            {
                var isCreator = admin.Status == ChatMemberStatus.Creator;
                var username = admin.User.Username; // Store Telegram username (without @)
                await chatAdminsRepository.UpsertAsync(chatId, admin.User.Id, isCreator, username);

                var displayName = username ?? admin.User.FirstName ?? admin.User.Id.ToString();
                adminNames.Add($"@{displayName}" + (isCreator ? " (creator)" : ""));
            }

            logger.LogInformation(
                "Cached {Count} admins for chat {ChatId}: {Admins}",
                admins.Length,
                chatId,
                string.Join(", ", adminNames));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to refresh admins for chat {ChatId}", chatId);
            throw; // Re-throw so caller can track failures
        }
    }

    private static string GetPermissionName(int level) => level switch
    {
        0 => "ReadOnly",
        1 => "Admin",
        2 => "Owner",
        _ => "Unknown"
    };

    /// <summary>
    /// Perform health check on a specific chat and update cache
    /// </summary>
    private async Task RefreshHealthForChatAsync(long chatId)
    {
        try
        {
            if (_botClient == null)
            {
                logger.LogWarning("Bot client not initialized, skipping health check for chat {ChatId}", chatId);
                return;
            }

            var (health, chatName) = await PerformHealthCheckAsync(_botClient, chatId);
            _healthCache[chatId] = health;
            OnHealthUpdate?.Invoke(health);

            // Update chat name in database if we got a valid title from Telegram
            if (health.IsReachable && !string.IsNullOrEmpty(chatName))
            {
                using var scope = serviceProvider.CreateScope();
                var managedChatsRepository = scope.ServiceProvider.GetRequiredService<IManagedChatsRepository>();
                var existingChat = await managedChatsRepository.GetByChatIdAsync(chatId);

                if (existingChat != null && existingChat.ChatName != chatName)
                {
                    // Update with fresh chat name from Telegram
                    var updatedChat = existingChat with { ChatName = chatName };
                    await managedChatsRepository.UpsertAsync(updatedChat);
                    logger.LogDebug("Updated chat name for {ChatId}: {OldName} -> {NewName}",
                        chatId, existingChat.ChatName, chatName);
                }
            }

            logger.LogDebug("Health check completed for chat {ChatId}: {Status}", chatId, health.Status);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to perform health check for chat {ChatId}", chatId);
        }
    }

    /// <summary>
    /// Perform health check on a chat (check reachability, permissions, etc.)
    /// Returns tuple of (health status, chat name)
    /// </summary>
    private async Task<(ChatHealthStatus Health, string? ChatName)> PerformHealthCheckAsync(ITelegramBotClient botClient, long chatId)
    {
        var health = new ChatHealthStatus
        {
            ChatId = chatId,
            IsReachable = false,
            Status = "Unknown"
        };
        string? chatName = null;

        try
        {
            // Try to get chat info
            var chat = await botClient.GetChat(chatId);
            health.IsReachable = true;
            chatName = chat.Title ?? chat.Username ?? $"Chat {chatId}";

            // Get bot's member status
            var botMember = await botClient.GetChatMember(chatId, botClient.BotId);
            health.BotStatus = botMember.Status.ToString();
            health.IsAdmin = botMember.Status == ChatMemberStatus.Administrator;

            // Check permissions if admin
            if (botMember.Status == ChatMemberStatus.Administrator && botMember is ChatMemberAdministrator admin)
            {
                health.CanDeleteMessages = admin.CanDeleteMessages;
                health.CanRestrictMembers = admin.CanRestrictMembers;
                health.CanPromoteMembers = admin.CanPromoteMembers;
                health.CanInviteUsers = admin.CanInviteUsers;
            }

            // Get admin count
            var admins = await botClient.GetChatAdministrators(chatId);
            health.AdminCount = admins.Length;

            // Determine overall status
            health.Status = "Healthy";
            health.Warnings.Clear();

            if (!health.IsAdmin)
            {
                health.Status = "Warning";
                health.Warnings.Add("Bot is not an admin in this chat");
            }
            else
            {
                if (!health.CanDeleteMessages)
                    health.Warnings.Add("Bot cannot delete messages");
                if (!health.CanRestrictMembers)
                    health.Warnings.Add("Bot cannot ban/restrict users");

                if (health.Warnings.Count > 0)
                    health.Status = "Warning";
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Health check failed for chat {ChatId}", chatId);
            health.IsReachable = false;
            health.Status = "Error";
            health.Warnings.Add($"Cannot reach chat: {ex.Message}");
        }

        return (health, chatName);
    }

    /// <summary>
    /// Refresh health for all managed chats
    /// </summary>
    private async Task RefreshAllHealthAsync()
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var managedChatsRepository = scope.ServiceProvider.GetRequiredService<IManagedChatsRepository>();
            var chats = await managedChatsRepository.GetAllChatsAsync();

            foreach (var chat in chats.Where(c => c.IsActive))
            {
                await RefreshHealthForChatAsync(chat.ChatId);
            }

            logger.LogInformation("Completed health check for {Count} chats", chats.Count(c => c.IsActive));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to refresh health for all chats");
        }
    }

    /// <summary>
    /// Phase 2.6: Determine if detection result should be used for training
    /// High-quality samples only: Confident OpenAI results (85%+) or manual admin decisions
    /// Low-confidence auto-detections are NOT training-worthy
    /// </summary>
    private static bool DetermineIfTrainingWorthy(TelegramGroupsAdmin.SpamDetection.Services.SpamDetectionResult result)
    {
        // Manual admin decisions are always training-worthy (will be set when admin uses Mark as Spam/Ham)
        // For auto-detections, only confident results are training-worthy

        // Check if OpenAI was involved and was confident (85%+ confidence)
        var openAIResult = result.CheckResults.FirstOrDefault(c => c.CheckName == "OpenAI");
        if (openAIResult != null)
        {
            // OpenAI confident (85%+) = training-worthy
            return openAIResult.Confidence >= 85;
        }

        // No OpenAI veto = borderline/uncertain detection
        // Only use for training if net confidence is very high (>80)
        // This prevents low-quality auto-detections from polluting training data
        return result.NetConfidence > 80;
    }

    /// <summary>
    /// Phase 2.7: Handle spam detection actions based on confidence levels
    /// - Net > +50 with OpenAI 85%+ confident → Auto-ban
    /// - Net > +50 with OpenAI <85% confident → Create report for admin review
    /// - Net +0 to +50 (borderline) → Create report for admin review
    /// - Net < 0 → No action (clean message)
    /// </summary>
    private async Task HandleSpamDetectionActionsAsync(
        IServiceProvider serviceProvider,
        Message message,
        TelegramGroupsAdmin.SpamDetection.Services.SpamDetectionResult spamResult,
        Models.DetectionResultRecord detectionResult)
    {
        try
        {
            // Only take action if spam was detected
            if (!spamResult.IsSpam || spamResult.NetConfidence <= 0)
            {
                return;
            }

            var reportsRepo = serviceProvider.GetRequiredService<IReportsRepository>();

            // Check if OpenAI was involved and how confident it was
            var openAIResult = spamResult.CheckResults.FirstOrDefault(c => c.CheckName == "OpenAI");
            var openAIConfident = openAIResult != null && openAIResult.Confidence >= 85;

            // Decision logic based on net confidence and OpenAI involvement
            if (spamResult.NetConfidence > 50 && openAIConfident && openAIResult!.IsSpam)
            {
                // Phase 2.7.2: Auto-ban implementation
                // High confidence + OpenAI confirmed = auto-ban across all managed chats
                logger.LogInformation(
                    "Message {MessageId} from user {UserId} in chat {ChatId} triggers auto-ban (net: {NetConfidence}, OpenAI: {OpenAIConf}%)",
                    message.MessageId,
                    message.From?.Id,
                    message.Chat.Id,
                    spamResult.NetConfidence,
                    openAIResult.Confidence);

                await ExecuteAutoBanAsync(
                    serviceProvider,
                    message,
                    spamResult,
                    openAIResult);
            }
            else if (spamResult.NetConfidence > 0)
            {
                // Borderline detection (0 < net ≤ 50) OR OpenAI uncertain (<85%) → Admin review
                var reason = spamResult.NetConfidence > 50
                    ? $"OpenAI uncertain (<85%) - Net: {spamResult.NetConfidence}, OpenAI: {openAIResult?.Confidence ?? 0}%"
                    : $"Borderline detection - Net: {spamResult.NetConfidence}";

                await CreateBorderlineReportAsync(
                    reportsRepo,
                    message,
                    spamResult,
                    detectionResult,
                    reason);

                logger.LogInformation(
                    "Created admin review report for message {MessageId} in chat {ChatId}: {Reason}",
                    message.MessageId,
                    message.Chat.Id,
                    reason);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to handle spam detection actions for message {MessageId}",
                message.MessageId);
        }
    }

    /// <summary>
    /// Phase 2.7: Create a report for borderline spam detections
    /// </summary>
    private static async Task CreateBorderlineReportAsync(
        IReportsRepository reportsRepo,
        Message message,
        TelegramGroupsAdmin.SpamDetection.Services.SpamDetectionResult spamResult,
        Models.DetectionResultRecord detectionResult,
        string reason)
    {
        var report = new Models.Report(
            Id: 0, // Will be assigned by database
            MessageId: (int)message.MessageId, // Convert to int (Telegram message IDs fit in int32)
            ChatId: message.Chat.Id,
            ReportCommandMessageId: null, // Auto-generated report (not from /report command)
            ReportedByUserId: null, // System-generated (not user-reported)
            ReportedByUserName: "Auto-Detection",
            ReportedAt: DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Status: Models.ReportStatus.Pending,
            ReviewedBy: null,
            ReviewedAt: null,
            ActionTaken: null,
            AdminNotes: $"{reason}\n\nDetection Details:\n{detectionResult.Reason}\n\nNet Confidence: {spamResult.NetConfidence}\nMax Confidence: {spamResult.MaxConfidence}",
            WebUserId: null // System-generated
        );

        await reportsRepo.InsertAsync(report);
    }

    /// <summary>
    /// Phase 2.7.2: Execute auto-ban for confident spam across all managed chats
    /// </summary>
    private async Task ExecuteAutoBanAsync(
        IServiceProvider serviceProvider,
        Message message,
        TelegramGroupsAdmin.SpamDetection.Services.SpamDetectionResult spamResult,
        TelegramGroupsAdmin.SpamDetection.Models.SpamCheckResponse openAIResult)
    {
        try
        {
            var userActionsRepo = serviceProvider.GetRequiredService<IUserActionsRepository>();
            var managedChatsRepo = serviceProvider.GetRequiredService<IManagedChatsRepository>();

            // Store ban action in database
            var banAction = new Models.UserActionRecord(
                Id: 0, // Will be assigned by database
                UserId: message.From!.Id,
                ActionType: Models.UserActionType.Ban,
                MessageId: message.MessageId,
                IssuedBy: "Auto-Detection",
                IssuedAt: DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ExpiresAt: null, // Permanent ban
                Reason: $"Auto-ban: High confidence spam (Net: {spamResult.NetConfidence}, OpenAI: {openAIResult.Confidence}%)"
            );

            await userActionsRepo.InsertAsync(banAction);

            // Get all managed chats for cross-chat enforcement
            var managedChats = await managedChatsRepo.GetAllChatsAsync();
            var activeChats = managedChats.Where(c => c.IsActive).ToList();

            logger.LogInformation(
                "Executing auto-ban for user {UserId} across {ChatCount} managed chats",
                message.From.Id,
                activeChats.Count);

            // Ban user across all managed chats via Telegram API
            int successCount = 0;
            int failCount = 0;

            foreach (var chat in activeChats)
            {
                try
                {
                    await _botClient!.BanChatMember(
                        chatId: chat.ChatId,
                        userId: message.From.Id,
                        untilDate: null, // Permanent ban
                        revokeMessages: true); // Delete all messages from this user

                    successCount++;

                    logger.LogInformation(
                        "Banned user {UserId} from chat {ChatId}",
                        message.From.Id,
                        chat.ChatId);
                }
                catch (Exception ex)
                {
                    failCount++;
                    logger.LogError(ex,
                        "Failed to ban user {UserId} from chat {ChatId}",
                        message.From.Id,
                        chat.ChatId);
                }
            }

            logger.LogInformation(
                "Auto-ban complete for user {UserId}: {SuccessCount}/{TotalCount} successful, {FailCount} failed",
                message.From.Id,
                successCount,
                activeChats.Count,
                failCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to execute auto-ban for user {UserId}",
                message.From?.Id);
        }
    }
}
