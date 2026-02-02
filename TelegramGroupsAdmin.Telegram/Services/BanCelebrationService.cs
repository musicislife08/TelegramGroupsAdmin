using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for posting celebratory GIFs when users are banned.
/// Sends GIF + caption to chat, optionally DMs the banned user.
/// Uses IBanCelebrationCache (singleton) for shuffle-bag state to ensure all GIFs/captions
/// are shown before any repeats.
/// Scoped service with direct dependency injection.
/// </summary>
public class BanCelebrationService(
    IConfigService configService,
    IBanCelebrationCache celebrationCache,
    IBanCelebrationGifRepository gifRepository,
    IBanCelebrationCaptionRepository captionRepository,
    IBotMessageService messageService,
    IBotDmService dmDeliveryService,
    IUserActionsRepository userActionsRepository,
    IOptions<MessageHistoryOptions> historyOptions,
    ILogger<BanCelebrationService> logger) : IBanCelebrationService
{
    private readonly string _mediaBasePath = Path.Combine(historyOptions.Value.ImageStoragePath, "media");

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
            var config = await configService.GetEffectiveAsync<BanCelebrationConfig>(
                ConfigType.BanCelebration, chatId);

            // Use default config if none exists
            config ??= BanCelebrationConfig.Default;

            // Check if feature is enabled
            if (!config.Enabled)
            {
                logger.LogDebug("Ban celebration disabled for chat {ChatId}", chatId);
                return false;
            }

            // Check trigger type
            if (isAutoBan && !config.TriggerOnAutoBan)
            {
                logger.LogDebug("Ban celebration skipped for auto-ban in chat {ChatId} (auto-ban trigger disabled)", chatId);
                return false;
            }

            if (!isAutoBan && !config.TriggerOnManualBan)
            {
                logger.LogDebug("Ban celebration skipped for manual ban in chat {ChatId} (manual ban trigger disabled)", chatId);
                return false;
            }

            // Get next GIF from shuffle bag (guarantees all shown before repeats)
            var gif = await GetNextGifAsync(cancellationToken);
            if (gif == null)
            {
                logger.LogDebug("No ban celebration GIFs available, skipping celebration");
                return false;
            }

            // Get next caption from shuffle bag
            var caption = await GetNextCaptionAsync(cancellationToken);
            if (caption == null)
            {
                logger.LogDebug("No ban celebration captions available, skipping celebration");
                return false;
            }

            // Get today's ban count for this chat
            var banCount = await GetTodaysBanCountAsync(cancellationToken);

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
                await gifRepository.UpdateFileIdAsync(gif.Id, sentMessage.Animation.FileId, cancellationToken);
            }

            logger.LogInformation(
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
            logger.LogWarning(ex, "Failed to send ban celebration for chat {ChatId}, user {UserId}", chatId, bannedUserId);
            return false;
        }
    }

    /// <summary>
    /// Gets the next GIF from the shuffle bag. When the bag is empty, reloads all GIF IDs
    /// from the database and shuffles them. This guarantees every GIF is shown once before
    /// any can repeat (minimum gap = total GIF count).
    /// </summary>
    private async Task<BanCelebrationGif?> GetNextGifAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            // Check if bag needs repopulating
            if (celebrationCache.IsGifBagEmpty)
            {
                var ids = await gifRepository.GetAllIdsAsync(cancellationToken);

                if (ids.Count == 0)
                    return null;

                celebrationCache.RepopulateGifBag(ids);
                logger.LogDebug("Reshuffled GIF bag with {Count} items", ids.Count);
            }

            var nextId = celebrationCache.GetNextGifId();
            if (nextId == null)
            {
                // Bag became empty between check and dequeue (race condition) - retry
                continue;
            }

            // Fetch the full GIF — may return null if deleted since last shuffle
            var gif = await gifRepository.GetByIdAsync(nextId.Value, cancellationToken);

            if (gif != null)
                return gif;

            logger.LogDebug("GIF {GifId} no longer exists, skipping to next in bag", nextId);
            // Continue loop — try next item in bag (or reshuffle if empty)
        }
    }

    /// <summary>
    /// Gets the next caption from the shuffle bag. Same algorithm as GIF bag.
    /// </summary>
    private async Task<BanCelebrationCaption?> GetNextCaptionAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            // Check if bag needs repopulating
            if (celebrationCache.IsCaptionBagEmpty)
            {
                var ids = await captionRepository.GetAllIdsAsync(cancellationToken);

                if (ids.Count == 0)
                    return null;

                celebrationCache.RepopulateCaptionBag(ids);
                logger.LogDebug("Reshuffled caption bag with {Count} items", ids.Count);
            }

            var nextId = celebrationCache.GetNextCaptionId();
            if (nextId == null)
            {
                // Bag became empty between check and dequeue (race condition) - retry
                continue;
            }

            // Fetch the full caption — may return null if deleted since last shuffle
            var caption = await captionRepository.GetByIdAsync(nextId.Value, cancellationToken);

            if (caption != null)
                return caption;

            logger.LogDebug("Caption {CaptionId} no longer exists, skipping to next in bag", nextId);
        }
    }

    private async Task<Message?> SendGifToChatAsync(
        long chatId,
        BanCelebrationGif gif,
        string caption,
        CancellationToken cancellationToken)
    {
        try
        {
            // Try cached file_id first (instant send)
            if (!string.IsNullOrEmpty(gif.FileId))
            {
                try
                {
                    var inputFile = InputFile.FromFileId(gif.FileId);
                    logger.LogDebug("Using cached file_id for GIF {GifId}", gif.Id);

                    return await messageService.SendAndSaveAnimationAsync(
                        chatId,
                        inputFile,
                        caption,
                        ParseMode.Markdown,
                        cancellationToken);
                }
                catch (Exception ex) when (IsInvalidFileIdError(ex))
                {
                    // Cached file_id is stale - clear it and fall back to local upload
                    logger.LogWarning(
                        "Cached file_id for GIF {GifId} is invalid, clearing cache and retrying with local file",
                        gif.Id);
                    await gifRepository.ClearFileIdAsync(gif.Id, cancellationToken);
                }
            }

            // Upload from local file (either no cache, or cache was invalid)
            var fullPath = gifRepository.GetFullPath(gif.FilePath);
            if (!File.Exists(fullPath))
            {
                logger.LogWarning("GIF file not found on disk: {Path}, GIF={GifId}", fullPath, gif.Id);
                return null;
            }

            await using var fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read);
            var fileName = Path.GetFileName(gif.FilePath);
            var localInputFile = InputFile.FromStream(fileStream, fileName);
            logger.LogDebug("Uploading GIF from disk: {Path}", fullPath);

            return await messageService.SendAndSaveAnimationAsync(
                chatId,
                localInputFile,
                caption,
                ParseMode.Markdown,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send GIF to chat {ChatId}", chatId);
            return null;
        }
    }

    private async Task TrySendDmToBannedUserAsync(
        long chatId,
        long bannedUserId,
        BanCelebrationGif gif,
        BanCelebrationCaption caption,
        string chatName,
        int banCount,
        CancellationToken cancellationToken)
    {
        try
        {
            // Check if the chat has DM-based welcome mode (required for DM delivery)
            var welcomeConfig = await configService.GetEffectiveAsync<WelcomeConfig>(ConfigType.Welcome, chatId);
            if (welcomeConfig == null || !welcomeConfig.Enabled)
            {
                logger.LogDebug("Skipping DM to banned user: welcome system not enabled for chat {ChatId}", chatId);
                return;
            }

            // Only DM if welcome mode is DM-based (DmWelcome or EntranceExam)
            if (welcomeConfig.Mode != WelcomeMode.DmWelcome && welcomeConfig.Mode != WelcomeMode.EntranceExam)
            {
                logger.LogDebug("Skipping DM to banned user: chat {ChatId} uses chat-based welcome mode", chatId);
                return;
            }

            // Build the DM caption (uses "You" grammar)
            // Escape for MarkdownV2 since DmDeliveryService uses that parse mode
            var dmCaption = TelegramTextUtilities.EscapeMarkdownV2(
                ReplacePlaceholders(caption.DmText, "You", chatName, banCount));

            // Get the full path to the GIF
            var fullPath = gifRepository.GetFullPath(gif.FilePath);
            if (!File.Exists(fullPath))
            {
                logger.LogWarning("GIF file not found for DM: {Path}", fullPath);
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

            var result = await dmDeliveryService.SendDmWithMediaAsync(
                bannedUserId,
                "ban_celebration",
                dmCaption,
                photoPath,
                videoPath,
                cancellationToken);

            if (result.DmSent)
            {
                logger.LogInformation("Ban celebration DM sent to banned user {UserId}", bannedUserId);
            }
            else
            {
                logger.LogDebug("Ban celebration DM failed for user {UserId}: {Error}",
                    bannedUserId, result.ErrorMessage ?? "Unknown error");
            }
        }
        catch (Exception ex)
        {
            // DM failures are expected (user blocked bot, never started, etc.) - just log and continue
            logger.LogDebug(ex, "Failed to send ban celebration DM to user {UserId}", bannedUserId);
        }
    }

    private async Task<int> GetTodaysBanCountAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await userActionsRepository.GetTodaysBanCountAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get today's ban count");
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

    /// <summary>
    /// Checks if the exception indicates an invalid/expired file_id.
    /// Telegram returns errors like "Bad Request: wrong file identifier" when file_ids become stale.
    /// </summary>
    private static bool IsInvalidFileIdError(Exception ex)
    {
        var message = ex.Message.ToLowerInvariant();
        return message.Contains("wrong file identifier") ||
               message.Contains("file_id") ||
               message.Contains("invalid file");
    }
}
