using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Services;
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
    IServiceScopeFactory scopeFactory,
    CommandRouter commandRouter,
    MessageProcessingService messageProcessingService,
    ChatManagementService chatManagementService,
    IWelcomeService welcomeService,
    ILogger<TelegramAdminBotService> logger)
    : BackgroundService, IMessageHistoryService
{
    private readonly TelegramOptions _options = options.Value;
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
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

    public event Action<long, TelegramGroupsAdmin.Telegram.Models.MediaType>? OnMediaUpdated
    {
        add => messageProcessingService.OnMediaUpdated += value;
        remove => messageProcessingService.OnMediaUpdated -= value;
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
        // Check if Telegram bot service is enabled (database-driven config)
        using (var scope = _scopeFactory.CreateScope())
        {
            var configService = scope.ServiceProvider.GetRequiredService<IConfigService>();
            var botConfig = await configService.GetAsync<TelegramBotConfig>(ConfigType.TelegramBot, null)
                           ?? TelegramBotConfig.Default;

            if (!botConfig.BotEnabled)
            {
                logger.LogInformation("Telegram bot service is disabled (BotEnabled=false in database config). Service will not start.");
                return;
            }
        }

        _botClient = botFactory.GetOrCreate(_options.BotToken);
        var botClient = _botClient;

        // Register bot commands in Telegram UI
        await RegisterBotCommandsAsync(botClient, stoppingToken);

        // Cache admin lists for all managed chats
        await chatManagementService.RefreshAllChatAdminsAsync(botClient, stoppingToken);

        // Perform initial health check for all chats
        await chatManagementService.RefreshAllHealthAsync(botClient);

        // Periodic health checks now handled by ChatHealthCheckJob via TickerQ recurring job scheduler
        // (see RecurringJobSchedulerService and BackgroundJobConfigService default config)

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

        // Reconnection loop with exponential backoff - retries indefinitely during outages
        const int maxRetryDelay = 60; // Cap at 60 seconds

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // If reconnecting after failure, log reconnection attempt
                if (!_isConnected)
                {
                    logger.LogInformation("Attempting to reconnect to Telegram (retry delay: {Delay}s)", _retryDelay.TotalSeconds);
                }

                await botClient.ReceiveAsync(
                    updateHandler: HandleUpdateAsync,
                    errorHandler: HandleErrorAsync,
                    receiverOptions: receiverOptions,
                    cancellationToken: stoppingToken);

                // If we reach here, ReceiveAsync ended gracefully (shouldn't happen unless cancelled)
                logger.LogInformation("Telegram bot polling ended gracefully");
                break;
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("Telegram bot stopped (cancellation requested)");
                break;
            }
            catch (Exception ex)
            {
                // Connection lost - log and retry with exponential backoff
                if (_isConnected)
                {
                    logger.LogError(ex, "Telegram bot connection lost - will retry automatically");
                    _isConnected = false;
                    _wasRecentlyDisconnected = true;
                }
                else
                {
                    logger.LogWarning(ex, "Telegram bot reconnection failed - retrying in {Delay}s", _retryDelay.TotalSeconds);
                }

                // Wait before retrying (exponential backoff, capped at 60s)
                try
                {
                    await Task.Delay(_retryDelay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    logger.LogInformation("Telegram bot stopped during reconnection delay");
                    break;
                }

                // Exponential backoff: 1s, 2s, 4s, 8s, 16s, 32s, 60s (capped)
                _retryDelay = TimeSpan.FromSeconds(Math.Min(_retryDelay.TotalSeconds * 2, maxRetryDelay));
            }
        }
    }

    private bool _wasRecentlyDisconnected = false;
    private TimeSpan _retryDelay = TimeSpan.FromSeconds(1);
    private bool _isConnected = true;

    private async Task HandleUpdateAsync(
        ITelegramBotClient botClient,
        Update update,
        CancellationToken cancellationToken)
    {
        // Log successful reconnection on first update after disconnect
        if (_wasRecentlyDisconnected)
        {
            logger.LogInformation("Telegram bot reconnected successfully - receiving updates");
            _wasRecentlyDisconnected = false;
            _isConnected = true;
            _retryDelay = TimeSpan.FromSeconds(1);
        }

        // Handle bot's chat member status changes (added/removed from chats)
        if (update.MyChatMember is { } myChatMember)
        {
            await chatManagementService.HandleMyChatMemberUpdateAsync(myChatMember);
            return;
        }

        // Handle user joins/leaves/promotions/demotions
        if (update.ChatMember is { } chatMember)
        {
            // Check for admin status changes (instant permission updates)
            await chatManagementService.HandleAdminStatusChangeAsync(chatMember, cancellationToken);

            // Handle joins/leaves (welcome system - Phase 4.4)
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
            await messageProcessingService.HandleEditedMessageAsync(botClient, editedMessage);
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

            // Default scope - commands for all users (Admin level 0)
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
