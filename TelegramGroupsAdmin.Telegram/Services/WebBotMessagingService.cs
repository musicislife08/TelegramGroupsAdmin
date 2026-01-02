using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for web UI bot messaging operations (Phase 1: Send & Edit Messages as Bot)
/// Encapsulates bot client management, feature availability, and signature logic
/// </summary>
public class WebBotMessagingService : IWebBotMessagingService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITelegramBotClientFactory _botFactory;
    private readonly IBotMessageService _botMessageService;
    private readonly ITelegramUserRepository _userRepo;
    private readonly ITelegramUserMappingRepository _mappingRepo;
    private readonly ITelegramBotService _botService;
    private readonly ILogger<WebBotMessagingService> _logger;

    public WebBotMessagingService(
        IServiceScopeFactory scopeFactory,
        ITelegramBotClientFactory botFactory,
        IBotMessageService botMessageService,
        ITelegramUserRepository userRepo,
        ITelegramUserMappingRepository mappingRepo,
        ITelegramBotService botService,
        ILogger<WebBotMessagingService> logger)
    {
        _scopeFactory = scopeFactory;
        _botFactory = botFactory;
        _botMessageService = botMessageService;
        _userRepo = userRepo;
        _mappingRepo = mappingRepo;
        _botService = botService;
        _logger = logger;
    }

    public async Task<WebBotFeatureAvailability> CheckFeatureAvailabilityAsync(
        string webUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check 1: Bot must be configured (check if bot token exists via factory)
            // TelegramConfigLoader.LoadConfigAsync() throws InvalidOperationException if token not configured
            try
            {
                await _botFactory.GetOperationsAsync();
            }
            catch (InvalidOperationException)
            {
                _logger.LogDebug("WebBotMessaging unavailable: Bot token not configured");
                return new WebBotFeatureAvailability(false, null, null, "Bot token not configured");
            }

            // Check 2: Bot must be enabled globally
            using var scope = _scopeFactory.CreateScope();
            var configService = scope.ServiceProvider.GetRequiredService<IConfigService>();
            var botConfig = await configService.GetAsync<TelegramBotConfig>(ConfigType.TelegramBot, 0);

            if (botConfig?.BotEnabled != true)
            {
                _logger.LogDebug("WebBotMessaging unavailable: Bot is disabled");
                return new WebBotFeatureAvailability(false, null, null, "Bot is disabled");
            }

            // Check 3: User must have linked Telegram account with username
            var (linkedUser, errorMessage) = await GetLinkedTelegramUserAsync(webUserId, cancellationToken);
            if (linkedUser == null)
            {
                return new WebBotFeatureAvailability(false, null, null, errorMessage);
            }

            // Get bot's user ID for identifying bot messages (use cached value from TelegramBotService)
            var botInfo = _botService.BotUserInfo;
            long? botUserId = botInfo?.Id;

            if (botUserId == null)
            {
                _logger.LogWarning("Bot user ID not available (bot may not be started yet)");
            }
            else
            {
                _logger.LogDebug("Bot user ID: {BotUserId}", botUserId);
            }

            _logger.LogInformation(
                "WebBotMessaging available for user {UserId} (Telegram: @{Username})",
                webUserId,
                linkedUser.Username);

            return new WebBotFeatureAvailability(true, botUserId, linkedUser.Username, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check feature availability for user {UserId}", webUserId);
            return new WebBotFeatureAvailability(false, null, null, $"Error checking availability: {ex.Message}");
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
            // Get linked Telegram user for signature
            var (linkedUser, errorMessage) = await GetLinkedTelegramUserAsync(webUserId, cancellationToken);
            if (linkedUser == null)
            {
                return new WebBotMessageResult(false, null, errorMessage);
            }

            // Validate input
            if (string.IsNullOrWhiteSpace(text))
            {
                return new WebBotMessageResult(false, null, "Message text cannot be empty");
            }

            // Append signature: \n\n—username
            var signature = $"\n\n—{linkedUser.Username}";
            var messageWithSignature = text + signature;

            // Validate total length (Telegram limit: 4096 characters)
            if (messageWithSignature.Length > 4096)
            {
                var maxTextLength = 4096 - signature.Length;
                return new WebBotMessageResult(false, null,
                    $"Message too long. Maximum length with signature is {maxTextLength} characters.");
            }

            // Build reply parameters if replying
            ReplyParameters? replyParameters = null;
            if (replyToMessageId.HasValue)
            {
                replyParameters = new ReplyParameters { MessageId = (int)replyToMessageId.Value };
            }

            // Send message via BotMessageService
            var sentMessage = await _botMessageService.SendAndSaveMessageAsync(
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
            // Validate input
            if (string.IsNullOrWhiteSpace(text))
            {
                return new WebBotMessageResult(false, null, "Message text cannot be empty");
            }

            // Validate length (Telegram limit: 4096 characters)
            if (text.Length > 4096)
            {
                return new WebBotMessageResult(false, null,
                    $"Message too long. Maximum length is 4096 characters (current: {text.Length}).");
            }

            // Edit message via BotMessageService (no signature added/modified)
            var editedMessage = await _botMessageService.EditAndUpdateMessageAsync(
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

    /// <summary>
    /// Get linked Telegram user for web user ID with validation
    /// </summary>
    /// <returns>Tuple of (TelegramUser, ErrorMessage) - user is null if validation fails</returns>
    private async Task<(TelegramUser? User, string? ErrorMessage)> GetLinkedTelegramUserAsync(
        string webUserId,
        CancellationToken cancellationToken)
    {
        var mappings = await _mappingRepo.GetByUserIdAsync(webUserId, cancellationToken);
        var linkedTelegramId = mappings.FirstOrDefault()?.TelegramId;

        if (!linkedTelegramId.HasValue)
        {
            _logger.LogDebug("WebBotMessaging unavailable for user {UserId}: No linked Telegram account", webUserId);
            return (null, "No linked Telegram account");
        }

        var linkedUser = await _userRepo.GetByTelegramIdAsync(linkedTelegramId.Value, cancellationToken);

        if (linkedUser == null || string.IsNullOrEmpty(linkedUser.Username))
        {
            _logger.LogDebug("WebBotMessaging unavailable for user {UserId}: Linked user not found or has no username", webUserId);
            return (null, "Linked Telegram account has no username");
        }

        return (linkedUser, null);
    }
}
