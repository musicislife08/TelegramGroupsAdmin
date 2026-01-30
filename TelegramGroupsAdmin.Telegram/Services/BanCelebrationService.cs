using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for posting celebratory GIFs when users are banned.
/// Sends GIF + caption to chat, optionally DMs the banned user.
/// Uses shuffle-bag algorithm to ensure all GIFs/captions are shown before any repeats.
/// </summary>
public class BanCelebrationService : IBanCelebrationService
{
    private readonly ITelegramBotClientFactory _botClientFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDmDeliveryService _dmDeliveryService;
    private readonly ILogger<BanCelebrationService> _logger;
    private readonly string _mediaBasePath;

    // Shuffle-bag state: guarantees all items shown before any repeats
    private readonly Queue<int> _gifBag = new();
    private readonly Queue<int> _captionBag = new();
    private readonly SemaphoreSlim _gifLock = new(1, 1);
    private readonly SemaphoreSlim _captionLock = new(1, 1);

    public BanCelebrationService(
        ITelegramBotClientFactory botClientFactory,
        IServiceScopeFactory scopeFactory,
        IDmDeliveryService dmDeliveryService,
        IOptions<MessageHistoryOptions> historyOptions,
        ILogger<BanCelebrationService> logger)
    {
        _botClientFactory = botClientFactory;
        _scopeFactory = scopeFactory;
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
            await using var configScope = _scopeFactory.CreateAsyncScope();
            var configService = configScope.ServiceProvider.GetRequiredService<IConfigService>();
            var config = await configService.GetEffectiveAsync<BanCelebrationConfig>(
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

            // Get next GIF from shuffle bag (guarantees all shown before repeats)
            var gif = await GetNextGifAsync(cancellationToken);
            if (gif == null)
            {
                _logger.LogDebug("No ban celebration GIFs available, skipping celebration");
                return false;
            }

            // Get next caption from shuffle bag
            var caption = await GetNextCaptionAsync(cancellationToken);
            if (caption == null)
            {
                _logger.LogDebug("No ban celebration captions available, skipping celebration");
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
                await using var cacheScope = _scopeFactory.CreateAsyncScope();
                var gifRepository = cacheScope.ServiceProvider.GetRequiredService<IBanCelebrationGifRepository>();
                await gifRepository.UpdateFileIdAsync(gif.Id, sentMessage.Animation.FileId, cancellationToken);
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

    /// <summary>
    /// Gets the next GIF from the shuffle bag. When the bag is empty, reloads all GIF IDs
    /// from the database and shuffles them. This guarantees every GIF is shown once before
    /// any can repeat (minimum gap = total GIF count).
    /// </summary>
    private async Task<BanCelebrationGif?> GetNextGifAsync(CancellationToken cancellationToken)
    {
        await _gifLock.WaitAsync(cancellationToken);
        try
        {
            while (true)
            {
                if (_gifBag.Count == 0)
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var gifRepository = scope.ServiceProvider.GetRequiredService<IBanCelebrationGifRepository>();
                    var ids = await gifRepository.GetAllIdsAsync(cancellationToken);

                    if (ids.Count == 0)
                        return null;

                    // Fisher-Yates shuffle
                    for (var i = ids.Count - 1; i > 0; i--)
                    {
                        var j = Random.Shared.Next(i + 1);
                        (ids[i], ids[j]) = (ids[j], ids[i]);
                    }

                    foreach (var id in ids)
                        _gifBag.Enqueue(id);

                    _logger.LogDebug("Reshuffled GIF bag with {Count} items", ids.Count);
                }

                var nextId = _gifBag.Dequeue();

                // Fetch the full GIF — may return null if deleted since last shuffle
                await using var fetchScope = _scopeFactory.CreateAsyncScope();
                var fetchRepo = fetchScope.ServiceProvider.GetRequiredService<IBanCelebrationGifRepository>();
                var gif = await fetchRepo.GetByIdAsync(nextId, cancellationToken);

                if (gif != null)
                    return gif;

                _logger.LogDebug("GIF {GifId} no longer exists, skipping to next in bag", nextId);
                // Continue loop — try next item in bag (or reshuffle if empty)
            }
        }
        finally
        {
            _gifLock.Release();
        }
    }

    /// <summary>
    /// Gets the next caption from the shuffle bag. Same algorithm as GIF bag.
    /// </summary>
    private async Task<BanCelebrationCaption?> GetNextCaptionAsync(CancellationToken cancellationToken)
    {
        await _captionLock.WaitAsync(cancellationToken);
        try
        {
            while (true)
            {
                if (_captionBag.Count == 0)
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var captionRepository = scope.ServiceProvider.GetRequiredService<IBanCelebrationCaptionRepository>();
                    var ids = await captionRepository.GetAllIdsAsync(cancellationToken);

                    if (ids.Count == 0)
                        return null;

                    // Fisher-Yates shuffle
                    for (var i = ids.Count - 1; i > 0; i--)
                    {
                        var j = Random.Shared.Next(i + 1);
                        (ids[i], ids[j]) = (ids[j], ids[i]);
                    }

                    foreach (var id in ids)
                        _captionBag.Enqueue(id);

                    _logger.LogDebug("Reshuffled caption bag with {Count} items", ids.Count);
                }

                var nextId = _captionBag.Dequeue();

                // Fetch the full caption — may return null if deleted since last shuffle
                await using var fetchScope = _scopeFactory.CreateAsyncScope();
                var fetchRepo = fetchScope.ServiceProvider.GetRequiredService<IBanCelebrationCaptionRepository>();
                var caption = await fetchRepo.GetByIdAsync(nextId, cancellationToken);

                if (caption != null)
                    return caption;

                _logger.LogDebug("Caption {CaptionId} no longer exists, skipping to next in bag", nextId);
            }
        }
        finally
        {
            _captionLock.Release();
        }
    }

    private async Task<Message?> SendGifToChatAsync(
        long chatId,
        BanCelebrationGif gif,
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
            // Try cached file_id first (instant send)
            if (!string.IsNullOrEmpty(gif.FileId))
            {
                try
                {
                    var inputFile = InputFile.FromFileId(gif.FileId);
                    _logger.LogDebug("Using cached file_id for GIF {GifId}", gif.Id);

                    return await telegramOps.SendAnimationAsync(
                        chatId,
                        inputFile,
                        caption,
                        ParseMode.Markdown,
                        cancellationToken);
                }
                catch (Exception ex) when (IsInvalidFileIdError(ex))
                {
                    // Cached file_id is stale - clear it and fall back to local upload
                    _logger.LogWarning(
                        "Cached file_id for GIF {GifId} is invalid, clearing cache and retrying with local file",
                        gif.Id);
                    await using var clearScope = _scopeFactory.CreateAsyncScope();
                    var gifRepository = clearScope.ServiceProvider.GetRequiredService<IBanCelebrationGifRepository>();
                    await gifRepository.ClearFileIdAsync(gif.Id, cancellationToken);
                }
            }

            // Upload from local file (either no cache, or cache was invalid)
            await using var pathScope = _scopeFactory.CreateAsyncScope();
            var pathRepo = pathScope.ServiceProvider.GetRequiredService<IBanCelebrationGifRepository>();
            var fullPath = pathRepo.GetFullPath(gif.FilePath);
            if (!System.IO.File.Exists(fullPath))
            {
                _logger.LogWarning("GIF file not found on disk: {Path}, GIF={GifId}", fullPath, gif.Id);
                return null;
            }

            await using var fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read);
            var fileName = Path.GetFileName(gif.FilePath);
            var localInputFile = InputFile.FromStream(fileStream, fileName);
            _logger.LogDebug("Uploading GIF from disk: {Path}", fullPath);

            return await telegramOps.SendAnimationAsync(
                chatId,
                localInputFile,
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
        BanCelebrationGif gif,
        BanCelebrationCaption caption,
        string chatName,
        int banCount,
        CancellationToken cancellationToken)
    {
        try
        {
            // Check if the chat has DM-based welcome mode (required for DM delivery)
            await using var scope = _scopeFactory.CreateAsyncScope();
            var configService = scope.ServiceProvider.GetRequiredService<IConfigService>();
            var welcomeConfig = await configService.GetEffectiveAsync<WelcomeConfig>(ConfigType.Welcome, chatId);
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
            // Escape for MarkdownV2 since DmDeliveryService uses that parse mode
            var dmCaption = TelegramTextUtilities.EscapeMarkdownV2(
                ReplacePlaceholders(caption.DmText, "You", chatName, banCount));

            // Get the full path to the GIF
            var gifRepository = scope.ServiceProvider.GetRequiredService<IBanCelebrationGifRepository>();
            var fullPath = gifRepository.GetFullPath(gif.FilePath);
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

    private async Task<int> GetTodaysBanCountAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var userActionsRepository = scope.ServiceProvider.GetRequiredService<IUserActionsRepository>();
            return await userActionsRepository.GetTodaysBanCountAsync(cancellationToken);
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
