using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Helpers;
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Identity;
using TelegramGroupsAdmin.Configuration.Services;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for posting celebratory GIFs when users are banned.
/// Posts to the chat when enabled and the trigger flags allow it, and queues delivery to the
/// chat's DM subscribers regardless of the chat post (subscribers get every celebration).
/// Optionally DMs the banned user directly.
/// Rotation is database-backed: each repository claims and stamps the next unclaimed row
/// atomically, then fetches it, so every GIF/caption is shown before any repeats, newly added
/// items are claimable immediately, and the rotation survives restarts.
/// Scoped service with direct dependency injection.
/// </summary>
public class BanCelebrationService(
    IConfigService configService,
    IBanCelebrationGifRepository gifRepository,
    IBanCelebrationCaptionRepository captionRepository,
    IUserIdentityService identityService,
    IBotMessageService messageService,
    IUserActionsRepository userActionsRepository,
    IBanCelebrationSubscriberRepository subscriberRepository,
    IUserNotificationService userNotificationService,
    ILogger<BanCelebrationService> logger,
    PipelineMetrics pipelineMetrics) : IBanCelebrationService
{
    public async Task<bool> SendBanCelebrationAsync(
        ChatIdentity chat,
        UserIdentity bannedUser,
        bool isAutoBan,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var config = await configService.GetEffectiveBanCelebrationAsync(chat.Id, cancellationToken)
                         ?? BanCelebrationConfig.Default;

            // The chat post honours Enabled + the trigger flags; DM subscribers get every celebration.
            var triggerAllowed = isAutoBan ? config.TriggerOnAutoBan : config.TriggerOnManualBan;
            var postToChat = config.Enabled && triggerAllowed;
            var hasSubscribers = await subscriberRepository.HasDeliverableSubscribersAsync(chat.Id, cancellationToken);

            // Rotation claims are durable DB stamps: never claim for a celebration nobody will see.
            if (!postToChat && !hasSubscribers)
            {
                logger.LogDebug("Ban celebration skipped for {Chat}: chat post off and no DM subscribers", chat.ToLogDebug());
                return false;
            }

            // Resolve the stored identity so the caption uses the latest names and name verdict,
            // whatever the caller's copy carried.
            bannedUser = await identityService.ResolveAsync(bannedUser.Id, cancellationToken);

            var gif = await GetNextGifAsync(cancellationToken);
            if (gif == null)
            {
                logger.LogDebug("No ban celebration GIFs available, skipping celebration");
                return false;
            }

            var caption = await GetNextCaptionAsync(cancellationToken);
            if (caption == null)
            {
                logger.LogDebug("No ban celebration captions available, skipping celebration");
                return false;
            }

            var banCount = await GetTodaysBanCountAsync(cancellationToken);

            // The chat's name-masking policy decides whether a flagged name is shown.
            var masking = await configService.GetNameMaskingAsync(chat.Id, cancellationToken);
            var displayedName = bannedUser.BotDisplayName(masking);

            if (displayedName != bannedUser.DisplayName)
            {
                logger.LogDebug("Masking flagged display name for {User} in {Chat}",
                    bannedUser.ToLogDebug(), chat.ToLogDebug());
                pipelineMetrics.RecordMaskedUsername(isAutoBan ? "auto_ban" : "manual_ban");
            }

            var chatCaption = ReplacePlaceholders(
                caption.Text,
                displayedName,
                chat.ChatName ?? chat.Id.ToString(),
                banCount);

            var delivered = false;

            if (postToChat)
            {
                var sentMessage = await SendGifToChatAsync(chat, gif, TelegramMessage.Plain(chatCaption), cancellationToken);
                if (sentMessage != null)
                {
                    if (string.IsNullOrEmpty(gif.FileId) && sentMessage.Animation?.FileId != null)
                    {
                        // The chat post already went out; a failed cache write must not skip the
                        // banned-user DM or the subscriber fan-out.
                        try
                        {
                            await gifRepository.UpdateFileIdAsync(gif.Id, sentMessage.Animation.FileId, cancellationToken);
                        }
                        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                        {
                            logger.LogWarning(ex, "Failed to cache file_id for ban celebration GIF {GifId}", gif.Id);
                        }
                    }

                    logger.LogInformation(
                        "Ban celebration sent to {Chat}: GIF={GifId}, Caption={CaptionId}, User={User}",
                        chat.ToLogInfo(), gif.Id, caption.Id, bannedUser.ToLogInfo());

                    if (config.SendToBannedUser)
                    {
                        await TrySendDmToBannedUserAsync(chat, bannedUser, gif, caption, banCount, cancellationToken);
                    }

                    delivered = true;
                }
            }

            if (hasSubscribers)
            {
                // Subscribers get exactly the chat's (masked) caption; the worker adds the chat header.
                await userNotificationService.EnqueueBanCelebrationAsync(chat, chatCaption, gif.Id, cancellationToken);
                delivered = true;
            }

            return delivered;
        }
        catch (Exception ex)
        {
            // Never fail the ban operation due to celebration errors
            logger.LogWarning(ex, "Failed to send ban celebration for {Chat}, user {User}",
                chat.ToLogDebug(), bannedUser.ToLogDebug());
            return false;
        }
    }

    /// <summary>
    /// Claims the next GIF in the rotation. The repository owns cycle state, so every GIF is sent
    /// once before any repeats, newly added GIFs are claimable immediately, and the rotation
    /// survives restarts.
    /// </summary>
    private Task<BanCelebrationGif?> GetNextGifAsync(CancellationToken cancellationToken) =>
        gifRepository.ClaimNextForCycleAsync(cancellationToken);

    /// <summary>
    /// Claims the next caption in the rotation. Same cycle semantics as <see cref="GetNextGifAsync"/>.
    /// </summary>
    private Task<BanCelebrationCaption?> GetNextCaptionAsync(CancellationToken cancellationToken) =>
        captionRepository.ClaimNextForCycleAsync(cancellationToken);

    private async Task<Message?> SendGifToChatAsync(
        ChatIdentity chat,
        BanCelebrationGif gif,
        TelegramMessage caption,
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
                        chat.Id,
                        inputFile,
                        caption,
                        cancellationToken);
                }
                catch (Exception ex) when (TelegramFileIdErrors.IsInvalidFileId(ex))
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
                chat.Id,
                localInputFile,
                caption,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send GIF to {Chat}", chat.ToLogDebug());
            return null;
        }
    }

    private async Task TrySendDmToBannedUserAsync(
        ChatIdentity chat,
        UserIdentity bannedUser,
        BanCelebrationGif gif,
        BanCelebrationCaption caption,
        int banCount,
        CancellationToken cancellationToken)
    {
        try
        {
            // Check if the chat has DM-based welcome mode (required for DM delivery)
            var welcomeConfig = await configService.GetEffectiveWelcomeAsync(chat.Id, cancellationToken);
            if (welcomeConfig == null || !welcomeConfig.Enabled)
            {
                logger.LogDebug("Skipping DM to banned user: welcome system not enabled for chat {ChatId}", chat.Id);
                return;
            }

            // Only DM if welcome mode is DM-based (DmWelcome or EntranceExam)
            if (welcomeConfig.Mode != WelcomeMode.DmWelcome && welcomeConfig.Mode != WelcomeMode.EntranceExam)
            {
                logger.LogDebug("Skipping DM to banned user: chat {ChatId} uses chat-based welcome mode", chat.Id);
                return;
            }

            // Build the DM caption (uses "You" grammar)
            var dmCaption = ReplacePlaceholders(caption.DmText, "You", chat.ChatName ?? chat.Id.ToString(), banCount);

            var sent = await userNotificationService.SendBanCelebrationToBannedUserAsync(
                chat, bannedUser, dmCaption, gif.Id, cancellationToken);

            if (sent)
            {
                logger.LogInformation("Ban celebration DM sent to banned user {User}", bannedUser.ToLogInfo());
            }
            else
            {
                logger.LogDebug("Ban celebration DM to banned user {UserId} was not delivered", bannedUser.Id);
            }
        }
        catch (Exception ex)
        {
            // DM failures are expected (user blocked bot, never started, etc.) - just log and continue
            logger.LogDebug(ex, "Failed to send ban celebration DM to user {UserId}", bannedUser.Id);
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
}
