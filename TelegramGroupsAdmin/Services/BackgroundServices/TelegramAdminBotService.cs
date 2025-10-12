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

            // TODO: Queue spam detection check using orchestrator (Phase 2.5)
            // The orchestrator handles trust/admin checks + spam detection in one place
            // For now, spam detection is only triggered manually via /spam command or via the test UI

            // Example of how to use the orchestrator when we implement auto spam detection:
            // using (var scope = serviceProvider.CreateScope())
            // {
            //     var orchestrator = scope.ServiceProvider.GetRequiredService<ISpamCheckOrchestrator>();
            //     var request = new SpamCheckRequest
            //     {
            //         Message = text ?? "",
            //         UserId = message.From.Id.ToString(),
            //         UserName = message.From.Username,
            //         ChatId = message.Chat.Id.ToString()
            //     };
            //     var result = await orchestrator.CheckAsync(request);
            //     if (result.IsSpam) { /* take action */ }
            // }
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
                await chatAdminsRepository.UpsertAsync(chatId, admin.User.Id, isCreator);

                var displayName = admin.User.Username ?? admin.User.FirstName ?? admin.User.Id.ToString();
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

            var health = await PerformHealthCheckAsync(_botClient, chatId);
            _healthCache[chatId] = health;
            OnHealthUpdate?.Invoke(health);

            logger.LogDebug("Health check completed for chat {ChatId}: {Status}", chatId, health.Status);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to perform health check for chat {ChatId}", chatId);
        }
    }

    /// <summary>
    /// Perform health check on a chat (check reachability, permissions, etc.)
    /// </summary>
    private async Task<ChatHealthStatus> PerformHealthCheckAsync(ITelegramBotClient botClient, long chatId)
    {
        var health = new ChatHealthStatus
        {
            ChatId = chatId,
            IsReachable = false,
            Status = "Unknown"
        };

        try
        {
            // Try to get chat info
            var chat = await botClient.GetChat(chatId);
            health.IsReachable = true;
            health.ChatTitle = chat.Title ?? chat.Username ?? $"Chat {chatId}";

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

        return health;
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
}
