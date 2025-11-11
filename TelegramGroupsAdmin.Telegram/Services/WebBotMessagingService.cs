using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Telegram.Abstractions.Services;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for web UI bot messaging operations (Phase 1: Send & Edit Messages as Bot)
/// Encapsulates bot client management, feature availability, and signature logic
/// </summary>
public class WebBotMessagingService : IWebBotMessagingService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TelegramBotClientFactory _botFactory;
    private readonly TelegramConfigLoader _configLoader;
    private readonly BotMessageService _botMessageService;
    private readonly ITelegramUserRepository _userRepo;
    private readonly ITelegramUserMappingRepository _mappingRepo;
    private readonly ILogger<WebBotMessagingService> _logger;

    public WebBotMessagingService(
        IServiceScopeFactory scopeFactory,
        TelegramBotClientFactory botFactory,
        TelegramConfigLoader configLoader,
        BotMessageService botMessageService,
        ITelegramUserRepository userRepo,
        ITelegramUserMappingRepository mappingRepo,
        ILogger<WebBotMessagingService> logger)
    {
        _scopeFactory = scopeFactory;
        _botFactory = botFactory;
        _configLoader = configLoader;
        _botMessageService = botMessageService;
        _userRepo = userRepo;
        _mappingRepo = mappingRepo;
        _logger = logger;
    }

    public async Task<WebBotFeatureAvailability> CheckFeatureAvailabilityAsync(
        string webUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check 1: Bot must be configured (check if bot token exists)
            var botToken = await _configLoader.LoadConfigAsync();
            if (string.IsNullOrEmpty(botToken))
            {
                _logger.LogDebug("WebBotMessaging unavailable: Bot token not configured");
                return new WebBotFeatureAvailability(false, null, "Bot token not configured");
            }

            // Check 2: Bot must be enabled globally
            using var scope = _scopeFactory.CreateScope();
            var configService = scope.ServiceProvider.GetRequiredService<IConfigService>();
            var botConfig = await configService.GetAsync<TelegramBotConfig>(ConfigType.TelegramBot, null);

            if (botConfig?.BotEnabled != true)
            {
                _logger.LogDebug("WebBotMessaging unavailable: Bot is disabled");
                return new WebBotFeatureAvailability(false, null, "Bot is disabled");
            }

            // Check 3: User must have linked Telegram account
            var mappings = await _mappingRepo.GetByUserIdAsync(webUserId, cancellationToken);
            var linkedTelegramId = mappings.FirstOrDefault()?.TelegramId;

            if (!linkedTelegramId.HasValue)
            {
                _logger.LogDebug("WebBotMessaging unavailable for user {UserId}: No linked Telegram account", webUserId);
                return new WebBotFeatureAvailability(false, null, "Link your Telegram account to send messages");
            }

            var linkedUser = await _userRepo.GetByTelegramIdAsync(linkedTelegramId.Value, cancellationToken);

            if (linkedUser == null || string.IsNullOrEmpty(linkedUser.Username))
            {
                _logger.LogDebug("WebBotMessaging unavailable for user {UserId}: Linked user not found or has no username", webUserId);
                return new WebBotFeatureAvailability(false, null, "Linked Telegram account has no username");
            }

            // Get bot's user ID for identifying bot messages
            long? botUserId = null;
            try
            {
                var botClient = _botFactory.GetOrCreate(botToken);
                var botInfo = await botClient.GetMe(cancellationToken);
                botUserId = botInfo.Id;
                _logger.LogDebug("Bot user ID: {BotUserId}", botUserId);
            }
            catch (Exception botEx)
            {
                _logger.LogWarning(botEx, "Failed to get bot user ID");
            }

            _logger.LogInformation(
                "WebBotMessaging available for user {UserId} (Telegram: @{Username})",
                webUserId,
                linkedUser.Username);

            return new WebBotFeatureAvailability(true, botUserId, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check feature availability for user {UserId}", webUserId);
            return new WebBotFeatureAvailability(false, null, $"Error checking availability: {ex.Message}");
        }
    }

    public async Task<WebBotMessageResult> SendMessageAsync(
        string webUserId,
        long chatId,
        string text,
        long? replyToMessageId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get bot token and client
            var botToken = await _configLoader.LoadConfigAsync();
            if (string.IsNullOrEmpty(botToken))
            {
                return new WebBotMessageResult(false, null, "Bot token not configured");
            }

            var botClient = _botFactory.GetOrCreate(botToken);

            // Get linked Telegram user for signature
            var mappings = await _mappingRepo.GetByUserIdAsync(webUserId, cancellationToken);
            var linkedTelegramId = mappings.FirstOrDefault()?.TelegramId;

            if (!linkedTelegramId.HasValue)
            {
                return new WebBotMessageResult(false, null, "No linked Telegram account");
            }

            var linkedUser = await _userRepo.GetByTelegramIdAsync(linkedTelegramId.Value, cancellationToken);

            if (linkedUser == null || string.IsNullOrEmpty(linkedUser.Username))
            {
                return new WebBotMessageResult(false, null, "Linked Telegram account has no username");
            }

            // Append signature: \n\n—username
            var signature = $"\n\n—{linkedUser.Username}";
            var messageWithSignature = text + signature;

            // Build reply parameters if replying
            ReplyParameters? replyParameters = null;
            if (replyToMessageId.HasValue)
            {
                replyParameters = new ReplyParameters { MessageId = (int)replyToMessageId.Value };
            }

            // Send message via BotMessageService
            var sentMessage = await _botMessageService.SendAndSaveMessageAsync(
                botClient,
                chatId,
                messageWithSignature,
                replyParameters: replyParameters,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Sent bot message {MessageId} from web user {UserId} (Telegram: @{Username})",
                sentMessage.MessageId,
                webUserId,
                linkedUser.Username);

            return new WebBotMessageResult(true, sentMessage, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send bot message for user {UserId}", webUserId);
            return new WebBotMessageResult(false, null, ex.Message);
        }
    }

    public async Task<WebBotMessageResult> EditMessageAsync(
        string webUserId,
        long chatId,
        int messageId,
        string text,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get bot token and client
            var botToken = await _configLoader.LoadConfigAsync();
            if (string.IsNullOrEmpty(botToken))
            {
                return new WebBotMessageResult(false, null, "Bot token not configured");
            }

            var botClient = _botFactory.GetOrCreate(botToken);

            // Edit message via BotMessageService (no signature added/modified)
            var editedMessage = await _botMessageService.EditAndUpdateMessageAsync(
                botClient,
                chatId,
                messageId,
                text,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Edited bot message {MessageId} from web user {UserId}",
                messageId,
                webUserId);

            return new WebBotMessageResult(true, editedMessage, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to edit bot message {MessageId} for user {UserId}", messageId, webUserId);
            return new WebBotMessageResult(false, null, ex.Message);
        }
    }
}
