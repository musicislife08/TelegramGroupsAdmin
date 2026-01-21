using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for posting celebratory GIFs when users are banned.
/// Sends random GIF + caption to chat, optionally DMs the banned user.
/// </summary>
public class BanCelebrationService : IBanCelebrationService
{
    private readonly ITelegramBotClientFactory _botClientFactory;
    private readonly IConfigService _configService;
    private readonly IBanCelebrationGifRepository _gifRepository;
    private readonly IBanCelebrationCaptionRepository _captionRepository;
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IDmDeliveryService _dmDeliveryService;
    private readonly ILogger<BanCelebrationService> _logger;
    private readonly string _mediaBasePath;

    public BanCelebrationService(
        ITelegramBotClientFactory botClientFactory,
        IConfigService configService,
        IBanCelebrationGifRepository gifRepository,
        IBanCelebrationCaptionRepository captionRepository,
        IDbContextFactory<AppDbContext> contextFactory,
        IDmDeliveryService dmDeliveryService,
        IOptions<MessageHistoryOptions> historyOptions,
        ILogger<BanCelebrationService> logger)
    {
        _botClientFactory = botClientFactory;
        _configService = configService;
        _gifRepository = gifRepository;
        _captionRepository = captionRepository;
        _contextFactory = contextFactory;
        _dmDeliveryService = dmDeliveryService;
        _logger = logger;
        _mediaBasePath = Path.Combine(historyOptions.Value.ImageStoragePath, "media");
    }

    public async Task<bool> SendBanCelebrationAsync(
        long chatId,
        string chatName,
        long bannedUserId,
        string bannedUserName,
        bool isAutoBan,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get the effective config for this chat (merges global + chat-specific)
            var config = await _configService.GetEffectiveAsync<BanCelebrationConfig>(
                ConfigType.BanCelebration, chatId);

            // Use default config if none exists
            config ??= BanCelebrationConfig.Default;

            // Check if feature is enabled
            if (!config.Enabled)
            {
                _logger.LogDebug("Ban celebration disabled for chat {ChatId}", chatId);
                return false;
            }

            // Check trigger type
            if (isAutoBan && !config.TriggerOnAutoBan)
            {
                _logger.LogDebug("Ban celebration skipped for auto-ban in chat {ChatId} (auto-ban trigger disabled)", chatId);
                return false;
            }

            if (!isAutoBan && !config.TriggerOnManualBan)
            {
                _logger.LogDebug("Ban celebration skipped for manual ban in chat {ChatId} (manual ban trigger disabled)", chatId);
                return false;
            }

            // Get random GIF
            var gif = await _gifRepository.GetRandomAsync(cancellationToken);
            if (gif == null)
            {
                _logger.LogDebug("No ban celebration GIFs available, skipping celebration");
                return false;
            }

            // Get random caption
            var caption = await _captionRepository.GetRandomAsync(cancellationToken);
            if (caption == null)
            {
                _logger.LogDebug("No ban celebration captions available, skipping celebration");
                return false;
            }

            // Get today's ban count for this chat
            var banCount = await GetTodaysBanCountAsync(chatId, cancellationToken);

            // Build the chat caption with placeholders replaced
            var chatCaption = ReplacePlaceholders(caption.Text, bannedUserName, chatName, banCount);

            // Send the GIF to the chat
            var sentMessage = await SendGifToChatAsync(chatId, gif, chatCaption, cancellationToken);
            if (sentMessage == null)
            {
                return false;
            }

            // Cache the file_id if we uploaded a new file
            if (string.IsNullOrEmpty(gif.FileId) && sentMessage.Animation?.FileId != null)
            {
                await _gifRepository.UpdateFileIdAsync(gif.Id, sentMessage.Animation.FileId, cancellationToken);
            }

            _logger.LogInformation(
                "Ban celebration sent to chat {ChatId}: GIF={GifId}, Caption={CaptionId}, User={UserId}",
                chatId, gif.Id, caption.Id, bannedUserId);

            // Optionally send DM to banned user
            if (config.SendToBannedUser)
            {
                await TrySendDmToBannedUserAsync(
                    chatId, bannedUserId, gif, caption, chatName, banCount, cancellationToken);
            }

            return true;
        }
        catch (Exception ex)
        {
            // Never fail the ban operation due to celebration errors
            _logger.LogWarning(ex, "Failed to send ban celebration for chat {ChatId}, user {UserId}", chatId, bannedUserId);
            return false;
        }
    }

    private async Task<Message?> SendGifToChatAsync(
        long chatId,
        Models.BanCelebrationGif gif,
        string caption,
        CancellationToken cancellationToken)
    {
        var telegramOps = await _botClientFactory.GetOperationsAsync();
        if (telegramOps == null)
        {
            _logger.LogWarning("Cannot send ban celebration: bot client not available");
            return null;
        }

        try
        {
            InputFile inputFile;

            // Use cached file_id if available (instant send)
            if (!string.IsNullOrEmpty(gif.FileId))
            {
                inputFile = InputFile.FromFileId(gif.FileId);
                _logger.LogDebug("Using cached file_id for GIF {GifId}", gif.Id);
            }
            else
            {
                // Upload from local file
                var fullPath = _gifRepository.GetFullPath(gif.FilePath);
                if (!System.IO.File.Exists(fullPath))
                {
                    _logger.LogWarning("GIF file not found on disk: {Path}, GIF={GifId}", fullPath, gif.Id);
                    return null;
                }

                var fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read);
                var fileName = Path.GetFileName(gif.FilePath);
                inputFile = InputFile.FromStream(fileStream, fileName);
                _logger.LogDebug("Uploading GIF from disk: {Path}", fullPath);
            }

            return await telegramOps.SendAnimationAsync(
                chatId,
                inputFile,
                caption,
                ParseMode.Markdown,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send GIF to chat {ChatId}", chatId);
            return null;
        }
    }

    private async Task TrySendDmToBannedUserAsync(
        long chatId,
        long bannedUserId,
        Models.BanCelebrationGif gif,
        Models.BanCelebrationCaption caption,
        string chatName,
        int banCount,
        CancellationToken cancellationToken)
    {
        try
        {
            // Check if the chat has DM-based welcome mode (required for DM delivery)
            var welcomeConfig = await _configService.GetEffectiveAsync<WelcomeConfig>(ConfigType.Welcome, chatId);
            if (welcomeConfig == null || !welcomeConfig.Enabled)
            {
                _logger.LogDebug("Skipping DM to banned user: welcome system not enabled for chat {ChatId}", chatId);
                return;
            }

            // Only DM if welcome mode is DM-based (DmWelcome or EntranceExam)
            if (welcomeConfig.Mode != WelcomeMode.DmWelcome && welcomeConfig.Mode != WelcomeMode.EntranceExam)
            {
                _logger.LogDebug("Skipping DM to banned user: chat {ChatId} uses chat-based welcome mode", chatId);
                return;
            }

            // Build the DM caption (uses "You" grammar)
            var dmCaption = ReplacePlaceholders(caption.DmText, "You", chatName, banCount);

            // Get the full path to the GIF
            var fullPath = _gifRepository.GetFullPath(gif.FilePath);
            if (!System.IO.File.Exists(fullPath))
            {
                _logger.LogWarning("GIF file not found for DM: {Path}", fullPath);
                return;
            }

            // Determine if it's a video or image for the DM service
            // The DM service uses SendDmWithMediaAsync which accepts photo/video paths
            // For GIFs/animations, we'll use the video path parameter
            var extension = Path.GetExtension(fullPath).ToLowerInvariant();
            string? photoPath = null;
            string? videoPath = null;

            if (extension is ".mp4" or ".gif")
            {
                videoPath = fullPath;
            }
            else
            {
                photoPath = fullPath;
            }

            var result = await _dmDeliveryService.SendDmWithMediaAsync(
                bannedUserId,
                "ban_celebration",
                dmCaption,
                photoPath,
                videoPath,
                cancellationToken);

            if (result.DmSent)
            {
                _logger.LogInformation("Ban celebration DM sent to banned user {UserId}", bannedUserId);
            }
            else
            {
                _logger.LogDebug("Ban celebration DM failed for user {UserId}: {Error}",
                    bannedUserId, result.ErrorMessage ?? "Unknown error");
            }
        }
        catch (Exception ex)
        {
            // DM failures are expected (user blocked bot, never started, etc.) - just log and continue
            _logger.LogDebug(ex, "Failed to send ban celebration DM to user {UserId}", bannedUserId);
        }
    }

    private async Task<int> GetTodaysBanCountAsync(long chatId, CancellationToken cancellationToken)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            // Get today's start in server local time, converted to UTC for PostgreSQL
            // PostgreSQL timestamptz only accepts UTC values via Npgsql
            var todayStart = new DateTimeOffset(DateTime.Today).ToUniversalTime();

            // Bans are global in this codebase (user_actions doesn't have chat_id)
            // Count all bans today as the daily counter
            return await context.UserActions
                .CountAsync(a =>
                    a.ActionType == Data.Models.UserActionType.Ban &&
                    a.IssuedAt >= todayStart,
                    cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get today's ban count");
            return 0;
        }
    }

    private static string ReplacePlaceholders(string text, string username, string chatName, int banCount)
    {
        return text
            .Replace("{username}", username, StringComparison.OrdinalIgnoreCase)
            .Replace("{chatname}", chatName, StringComparison.OrdinalIgnoreCase)
            .Replace("{bancount}", banCount.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
