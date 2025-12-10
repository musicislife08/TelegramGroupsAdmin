using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Services.BotCommands;
using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.Telegram.Services.BackgroundServices;

/// <summary>
/// Core Telegram bot service - handles bot lifecycle and routes updates to specialized services
/// </summary>
public class TelegramAdminBotService(
    TelegramBotClientFactory botFactory,
    TelegramConfigLoader configLoader,
    IServiceScopeFactory scopeFactory,
    CommandRouter commandRouter,
    MessageProcessingService messageProcessingService,
    ChatManagementService chatManagementService,
    IWelcomeService welcomeService,
    ILogger<TelegramAdminBotService> logger)
    : BackgroundService, IMessageHistoryService
{
    private readonly TelegramConfigLoader _configLoader = configLoader;
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private ITelegramBotClient? _botClient;
    private CancellationTokenSource? _botCancellationTokenSource;
    private Task? _botTask;
    private readonly SemaphoreSlim _configChangeSignal = new(0, 1);
    private User? _botUserInfo; // Cached bot user info from GetMe()

    // Cached configuration loaded once at startup
    private string? _botToken;

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

    public event Action<long, MediaType>? OnMediaUpdated
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
    /// Get cached bot user info from GetMe() (available after service starts)
    /// </summary>
    public User? BotUserInfo => _botUserInfo;

    /// <summary>
    /// Get cached health status for a chat (null if not yet checked)
    /// </summary>
    public ChatHealthStatus? GetCachedHealth(long chatId)
        => chatManagementService.GetCachedHealth(chatId);

    /// <summary>
    /// Notify the bot service that configuration has changed - triggers immediate config check and bot state refresh
    /// </summary>
    public void NotifyConfigChange()
    {
        // Release the semaphore to wake up the monitoring loop
        // CurrentCount check prevents multiple releases (semaphore has maxCount=1)
        if (_configChangeSignal.CurrentCount == 0)
        {
            _configChangeSignal.Release();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial config check on startup
        await CheckAndApplyBotConfigAsync(stoppingToken);

        // Wait for config change notifications (event-driven, no polling)
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Block until NotifyConfigChange() is called or cancellation requested
                await _configChangeSignal.WaitAsync(stoppingToken);

                // Config changed - check and apply new state
                await CheckAndApplyBotConfigAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        // Cleanup on shutdown
        if (_botCancellationTokenSource != null)
        {
            _botCancellationTokenSource.Cancel();
            if (_botTask != null)
            {
                await _botTask;
            }
            _botCancellationTokenSource.Dispose();
        }
    }

    private async Task CheckAndApplyBotConfigAsync(CancellationToken stoppingToken)
    {
        // Check if bot should be running
        TelegramBotConfig botConfig;
        using (var scope = _scopeFactory.CreateScope())
        {
            var configService = scope.ServiceProvider.GetRequiredService<IConfigService>();
            // Load global bot config (chat_id = 0 for global config)
            botConfig = await configService.GetAsync<TelegramBotConfig>(ConfigType.TelegramBot, 0)
                       ?? TelegramBotConfig.Default;
        }

        var shouldBeRunning = botConfig.BotEnabled;
        var isCurrentlyRunning = _botTask != null && !_botTask.IsCompleted;

        if (shouldBeRunning && !isCurrentlyRunning)
        {
            // Start the bot
            logger.LogInformation("Starting Telegram bot service (BotEnabled=true)");
            _botCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            _botTask = RunBotAsync(_botCancellationTokenSource.Token);
        }
        else if (!shouldBeRunning && isCurrentlyRunning)
        {
            // Stop the bot
            logger.LogInformation("Stopping Telegram bot service (BotEnabled=false)");
            _botCancellationTokenSource?.Cancel();
            if (_botTask != null)
            {
                await _botTask;
            }
            _botCancellationTokenSource?.Dispose();
            _botCancellationTokenSource = null;
            _botTask = null;
            _botClient = null;
            logger.LogInformation("Telegram bot service stopped successfully");
        }
    }

    private async Task RunBotAsync(CancellationToken stoppingToken)
    {
        // Load configuration from database (if not already loaded)
        if (_botToken == null)
        {
            _botToken = await _configLoader.LoadConfigAsync();
            logger.LogInformation("Loaded Telegram bot configuration from database");
        }

        _botClient = botFactory.GetOrCreate(_botToken);
        var botClient = _botClient;

        // Fetch and cache bot info
        var me = await botClient.GetMe(stoppingToken);
        _botUserInfo = me; // Cache full bot info for BotMessageService
        logger.LogInformation("Bot user ID cached: {BotUserId} (@{BotUsername})", me.Id, me.Username);

        // Register bot commands in Telegram UI
        await RegisterBotCommandsAsync(botClient, stoppingToken);

        // Cache admin lists for all managed chats
        await chatManagementService.RefreshAllChatAdminsAsync(botClient, stoppingToken);

        // Perform initial health check for all chats
        await chatManagementService.RefreshAllHealthAsync(botClient);

        // Periodic health checks now handled by ChatHealthCheckJob via Quartz.NET recurring job scheduler
        // (see QuartzSchedulingSyncService and BackgroundJobConfigService default config)

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

        // Start long polling (runs until cancellation or catastrophic error)
        // Network errors during polling are handled by HandleErrorAsync with exponential backoff
        try
        {
            await botClient.ReceiveAsync(
                updateHandler: HandleUpdateAsync,
                errorHandler: HandleErrorAsync,
                receiverOptions: receiverOptions,
                cancellationToken: stoppingToken);

            // If we reach here, ReceiveAsync ended gracefully (shouldn't happen unless cancelled)
            logger.LogInformation("Telegram bot polling ended gracefully");
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Telegram bot stopped (cancellation requested)");
        }
        catch (Exception ex)
        {
            // Catastrophic error (ReceiveAsync itself threw, not a polling error)
            logger.LogCritical(ex, "Telegram bot encountered catastrophic error and cannot continue");
        }
    }

    private bool _wasRecentlyDisconnected;
    private bool _isConnected = true;
    private int _consecutiveErrors;

    private async Task HandleUpdateAsync(
        ITelegramBotClient botClient,
        Update update,
        CancellationToken cancellationToken)
    {
        // Log successful reconnection on first update after disconnect
        if (_wasRecentlyDisconnected)
        {
            logger.LogInformation("Telegram bot reconnected successfully");
            _wasRecentlyDisconnected = false;
            _isConnected = true;
            _consecutiveErrors = 0;
        }

        // Handle bot's chat member status changes (added/removed from chats)
        if (update.MyChatMember is { } myChatMember)
        {
            await chatManagementService.HandleMyChatMemberUpdateAsync(botClient, myChatMember, cancellationToken);
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

    private async Task HandleErrorAsync(
        ITelegramBotClient botClient,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // Track connection state
        if (_isConnected)
        {
            logger.LogInformation("Telegram bot connection lost - will retry automatically");
            _isConnected = false;
            _wasRecentlyDisconnected = true;
        }

        _consecutiveErrors++;

        // Exponential backoff: 1s, 2s, 4s, 8s, 16s, 32s, 60s (capped)
        const int maxRetryDelay = 60;
        var delaySeconds = Math.Min(Math.Pow(2, _consecutiveErrors - 1), maxRetryDelay);

        // Log retry attempt (clean message, no exception details)
        logger.LogInformation("Telegram bot retrying connection in {Delay}s (attempt {Attempt})", (int)delaySeconds, _consecutiveErrors);

        // Wait before next retry attempt (exponential backoff)
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Service stopping, that's fine
        }
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
