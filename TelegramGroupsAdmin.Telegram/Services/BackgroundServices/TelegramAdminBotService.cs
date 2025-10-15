using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Services.BotCommands;
using TelegramGroupsAdmin.Telegram.Abstractions.Services;

namespace TelegramGroupsAdmin.Telegram.Services.BackgroundServices;

/// <summary>
/// Core Telegram bot service - handles bot lifecycle and routes updates to specialized services
/// </summary>
public class TelegramAdminBotService(
    TelegramBotClientFactory botFactory,
    IOptions<TelegramOptions> options,
    IOptions<MessageHistoryOptions> historyOptions,
    CommandRouter commandRouter,
    MessageProcessingService messageProcessingService,
    ChatManagementService chatManagementService,
    IWelcomeService welcomeService,
    ILogger<TelegramAdminBotService> logger)
    : BackgroundService, IMessageHistoryService
{
    private readonly TelegramOptions _options = options.Value;
    private readonly MessageHistoryOptions _historyOptions = historyOptions.Value;
    private ITelegramBotClient? _botClient;

    // Events for real-time UI updates (forwarded from child services)
    public event Action<MessageRecord>? OnNewMessage
    {
        add => messageProcessingService.OnNewMessage += value;
        remove => messageProcessingService.OnNewMessage -= value;
    }

    public event Action<MessageEditRecord>? OnMessageEdited
    {
        add => messageProcessingService.OnMessageEdited += value;
        remove => messageProcessingService.OnMessageEdited -= value;
    }

    public event Action<ChatHealthStatus>? OnHealthUpdate
    {
        add => chatManagementService.OnHealthUpdate += value;
        remove => chatManagementService.OnHealthUpdate -= value;
    }

    /// <summary>
    /// Get the bot client instance (available after service starts)
    /// </summary>
    public ITelegramBotClient? BotClient => _botClient;

    /// <summary>
    /// Get cached health status for a chat (null if not yet checked)
    /// </summary>
    public ChatHealthStatus? GetCachedHealth(long chatId)
        => chatManagementService.GetCachedHealth(chatId);

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
        await chatManagementService.RefreshAllChatAdminsAsync(botClient, stoppingToken);

        // Perform initial health check for all chats
        await chatManagementService.RefreshAllHealthAsync(botClient);

        // Start periodic health check timer (runs every 1 minute)
        _ = Task.Run(async () =>
        {
            var healthTimer = new PeriodicTimer(TimeSpan.FromMinutes(1));
            try
            {
                while (await healthTimer.WaitForNextTickAsync(stoppingToken))
                {
                    await chatManagementService.RefreshAllHealthAsync(botClient);
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
            AllowedUpdates = [
                UpdateType.Message,           // New messages (commands, text, photos)
                UpdateType.EditedMessage,     // Message edits (spam tactic detection)
                UpdateType.MyChatMember,      // Bot added/removed from chats
                UpdateType.ChatMember,        // User joins/leaves chat (for welcome system)
                UpdateType.CallbackQuery      // Inline button clicks (for welcome accept/deny)
            ],
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
            await chatManagementService.HandleMyChatMemberUpdateAsync(myChatMember);
            return;
        }

        // Handle user joins/leaves (for welcome system - Phase 4.4)
        if (update.ChatMember is { } chatMember)
        {
            await welcomeService.HandleChatMemberUpdateAsync(botClient, chatMember, cancellationToken);
            return;
        }

        // Handle callback queries from inline buttons (for welcome accept/deny - Phase 4.4)
        if (update.CallbackQuery is { } callbackQuery)
        {
            await welcomeService.HandleCallbackQueryAsync(botClient, callbackQuery, cancellationToken);

            // Always answer callback queries to remove loading state
            await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
            return;
        }

        // Handle new messages
        if (update.Message is { } message)
        {
            await messageProcessingService.HandleNewMessageAsync(botClient, message);
            return;
        }

        // Handle edited messages
        if (update.EditedMessage is { } editedMessage)
        {
            await messageProcessingService.HandleEditedMessageAsync(editedMessage);
            return;
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
}
