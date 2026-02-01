using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Telegram.Services.BotCommands;
using TelegramGroupsAdmin.Telegram.Constants;

namespace TelegramGroupsAdmin.Telegram.Services.BackgroundServices;

/// <summary>
/// Thin BackgroundService that manages the Telegram polling lifecycle.
/// Only ONE polling connection per bot token (Telegram API constraint).
/// Bot capabilities (events, state) are handled by TelegramBotService.
/// Update routing is handled by UpdateRouter.
/// </summary>
public class TelegramBotPollingHost(
    ITelegramBotClientFactory botFactory,
    IUpdateRouter updateRouter,
    ITelegramBotService botService,
    IBotChatHealthService chatHealthService,
    CommandRouter commandRouter,
    IServiceScopeFactory scopeFactory,
    ILogger<TelegramBotPollingHost> logger) : BackgroundService
{
    private ITelegramBotClient? _botClient;
    private CancellationTokenSource? _botCancellationTokenSource;
    private Task? _botTask;
    private readonly SemaphoreSlim _configChangeSignal = new(0, 1);

    // Connection state for logging
    private bool _wasRecentlyDisconnected;
    private bool _isConnected = true;
    private int _consecutiveErrors;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Subscribe to config change requests from TelegramBotService
        botService.ConfigChangeRequested += OnConfigChangeRequested;

        try
        {
            // Initial config check on startup
            await CheckAndApplyBotConfigAsync(stoppingToken);

            // Wait for config change notifications (event-driven, no polling)
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Block until ConfigChangeRequested event or cancellation requested
                    await _configChangeSignal.WaitAsync(stoppingToken);

                    // Config changed - check and apply new state
                    await CheckAndApplyBotConfigAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        finally
        {
            botService.ConfigChangeRequested -= OnConfigChangeRequested;
            await StopBotAsync();
            _configChangeSignal.Dispose();
        }
    }

    private void OnConfigChangeRequested()
    {
        // Release the semaphore to wake up the monitoring loop
        // CurrentCount check prevents multiple releases (semaphore has maxCount=1)
        if (_configChangeSignal.CurrentCount == 0)
        {
            _configChangeSignal.Release();
        }
    }

    private async Task CheckAndApplyBotConfigAsync(CancellationToken stoppingToken)
    {
        // Check if bot should be running
        TelegramBotConfig botConfig;
        using (var scope = scopeFactory.CreateScope())
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
            logger.LogInformation("Starting Telegram bot polling (BotEnabled=true)");
            _botCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            _botTask = RunBotAsync(_botCancellationTokenSource.Token);
        }
        else if (!shouldBeRunning && isCurrentlyRunning)
        {
            // Stop the bot
            logger.LogInformation("Stopping Telegram bot polling (BotEnabled=false)");
            await StopBotAsync();
            logger.LogInformation("Telegram bot polling stopped successfully");
        }
    }

    private async Task StopBotAsync()
    {
        if (_botCancellationTokenSource != null)
        {
            await _botCancellationTokenSource.CancelAsync();
            if (_botTask != null)
            {
                await _botTask;
            }
            _botCancellationTokenSource.Dispose();
            _botCancellationTokenSource = null;
        }
        _botTask = null;
        _botClient = null;

        // Clear bot info when stopped
        botService.SetBotUserInfo(null);
    }

    private async Task RunBotAsync(CancellationToken stoppingToken)
    {
        // Reset connection state for fresh start (prevents stale backoff from previous run)
        _isConnected = true;
        _wasRecentlyDisconnected = false;
        _consecutiveErrors = 0;

        // Get bot client from factory (factory handles token loading internally)
        _botClient = await botFactory.GetBotClientAsync();
        var botClient = _botClient;

        // Fetch and cache bot info via TelegramBotService (logs identity internally)
        var me = await botClient.GetMe(stoppingToken);
        botService.SetBotUserInfo(me);

        // Register bot commands in Telegram UI
        await RegisterBotCommandsAsync(botClient, stoppingToken);

        // Cache admin lists for all managed chats
        await chatHealthService.RefreshAllChatAdminsAsync(stoppingToken);

        // Perform initial health check for all chats
        await chatHealthService.RefreshAllHealthAsync(stoppingToken);

        // Periodic health checks handled by ChatHealthCheckJob via Quartz.NET
        logger.LogInformation("Telegram bot started listening for messages in all chats");

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates =
            [
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

        // Delegate to UpdateRouter for testable routing
        await updateRouter.RouteUpdateAsync(update, cancellationToken);
    }

    private Task HandleErrorAsync(
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
        var delaySeconds = Math.Min(Math.Pow(ConnectionConstants.ExponentialBackoffBase, _consecutiveErrors - ConnectionConstants.ConnectionAttemptOffset), ConnectionConstants.MaxRetryDelaySeconds);

        // Log retry attempt (clean message, no exception details)
        logger.LogInformation("Telegram bot retrying connection in {Delay}s (attempt {Attempt})", (int)delaySeconds, _consecutiveErrors);

        // Wait before next retry attempt (exponential backoff)
        return Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
    }

    private async Task RegisterBotCommandsAsync(ITelegramBotClient botClient, CancellationToken cancellationToken)
    {
        try
        {
            // Register commands with different scopes based on permission levels

            // Default scope - commands for all users (Admin level 0)
            var defaultCommands = commandRouter.GetAvailableCommands(permissionLevel: CommandConstants.DefaultCommandPermissionLevel)
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
            var adminCommands = commandRouter.GetAvailableCommands(permissionLevel: CommandConstants.AdminCommandPermissionLevel)
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
