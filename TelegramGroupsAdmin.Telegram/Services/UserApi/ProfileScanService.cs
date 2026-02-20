using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.JobPayloads;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Core.Services.AI;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using TL;

namespace TelegramGroupsAdmin.Telegram.Services.UserApi;

/// <summary>
/// Central orchestrator for profile scanning. Calls WTelegram API, runs scoring,
/// persists results, and takes moderation action (ban/report).
/// Singleton — uses IServiceScopeFactory for scoped dependency access.
/// </summary>
public sealed class ProfileScanService(
    ITelegramSessionManager sessionManager,
    IServiceScopeFactory scopeFactory,
    ILogger<ProfileScanService> logger) : IProfileScanService
{
    /// <summary>
    /// Skip re-scanning if profile was scanned within this window.
    /// Multi-chat dedup: if user joins two chats simultaneously, only one scan runs.
    /// </summary>
    private static readonly TimeSpan ScanFreshnessWindow = TimeSpan.FromSeconds(60);

    public async Task<ProfileScanResult> ScanUserProfileAsync(
        UserIdentity user,
        ChatIdentity? triggeringChat,
        CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var userRepo = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();

        // ── Multi-chat dedup: skip if recently scanned ──
        var existingUser = await userRepo.GetByTelegramIdAsync(user.Id, ct);
        if (existingUser?.ProfileScannedAt is { } lastScan
            && DateTimeOffset.UtcNow - lastScan < ScanFreshnessWindow
            && existingUser.ProfileScanScore.HasValue)
        {
            logger.LogDebug("Profile scan for {User}: recently scanned ({LastScan}), reusing cached score {Score}",
                user.ToLogDebug(), lastScan, existingUser.ProfileScanScore);

            var cachedOutcome = await DetermineOutcomeAsync(existingUser.ProfileScanScore.Value, triggeringChat, scope.ServiceProvider, ct);
            return new ProfileScanResult(
                user.Id,
                existingUser.Bio, existingUser.PersonalChannelId,
                existingUser.PersonalChannelTitle, existingUser.PersonalChannelAbout,
                existingUser.HasPinnedStories, existingUser.PinnedStoryCaptions,
                existingUser.IsScam, existingUser.IsFake, existingUser.IsVerified,
                existingUser.ProfileScanScore.Value, cachedOutcome, null, null);
        }

        // ── Get User API client ──
        var client = await sessionManager.GetAnyClientAsync(ct);
        if (client == null)
        {
            logger.LogWarning("No User API client available for profile scan of {User}", user.ToLogDebug());
            return EmptyResult(user.Id);
        }

        // ── Step 1: Fetch full user info ──
        Users_UserFull fullUser;
        try
        {
            fullUser = await client.Users_GetFullUser(new InputUser(user.Id, 0));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get full user info for {User}", user.ToLogDebug());
            return EmptyResult(user.Id);
        }

        var userInfo = fullUser.full_user;
        var tlUser = fullUser.users.Values.OfType<TL.User>().FirstOrDefault(u => u.id == user.Id);

        var bio = userInfo?.about;
        var personalChannelId = userInfo?.personal_channel_id;
        // stories_pinned_available is on UserFull.Flags, not User.Flags
        var hasPinnedStories = userInfo?.flags.HasFlag(UserFull.Flags.stories_pinned_available) == true;
        // scam, fake, verified are on User.flags enum
        var isScam = tlUser?.flags.HasFlag(TL.User.Flags.scam) == true;
        var isFake = tlUser?.flags.HasFlag(TL.User.Flags.fake) == true;
        var isVerified = tlUser?.flags.HasFlag(TL.User.Flags.verified) == true;

        // ── Step 2: Resolve personal channel ──
        string? channelTitle = null;
        string? channelAbout = null;
        if (personalChannelId is > 0)
        {
            try
            {
                var channelFull = await client.Channels_GetFullChannel(new InputChannel(personalChannelId.Value, 0));
                var channelChat = channelFull.chats.Values.OfType<Channel>().FirstOrDefault();
                channelTitle = channelChat?.title;
                if (channelFull.full_chat is ChannelFull cf)
                    channelAbout = cf.about;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to resolve personal channel {ChannelId} for {User}",
                    personalChannelId, user.ToLogDebug());
            }
        }

        // ── Step 3: Fetch pinned stories ──
        string? pinnedStoryCaptions = null;
        int storyCount = 0;
        List<string>? storyCaptions = null;
        if (hasPinnedStories)
        {
            try
            {
                var peerStories = await client.Stories_GetPeerStories(new InputPeerUser(user.Id, 0));
                var stories = peerStories.stories?.stories;
                if (stories is { Length: > 0 })
                {
                    storyCount = stories.Length;
                    storyCaptions = stories
                        .OfType<StoryItem>()
                        .Where(s => !string.IsNullOrEmpty(s.caption))
                        .Select(s => s.caption)
                        .ToList();
                    if (storyCaptions.Count > 0)
                        pinnedStoryCaptions = string.Join("\n", storyCaptions);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to fetch pinned stories for {User}", user.ToLogDebug());
            }
        }

        // ── Step 4: Collect images for AI vision ──
        var images = await CollectImagesAsync(existingUser?.UserPhotoPath, ct);

        // ── Step 5: Run scoring engine ──
        var chat = triggeringChat ?? ChatIdentity.FromId(0);
        var profileData = new ProfileData(
            user, chat,
            tlUser?.first_name, tlUser?.last_name, tlUser?.MainUsername,
            bio, personalChannelId, channelTitle, channelAbout,
            hasPinnedStories, pinnedStoryCaptions, storyCount, storyCaptions,
            isScam, isFake, isVerified);

        var configService = scope.ServiceProvider.GetRequiredService<IConfigService>();
        var welcomeConfig = await configService.GetEffectiveAsync<WelcomeConfig>(ConfigType.Welcome, chat.Id);
        var profileScanConfig = welcomeConfig?.JoinSecurity?.ProfileScan;
        var banThreshold = profileScanConfig?.BanThreshold ?? 4.0m;
        var notifyThreshold = profileScanConfig?.NotifyThreshold ?? 2.0m;

        var scoringEngine = new ProfileScoringEngine(
            scope.ServiceProvider.GetRequiredService<IUrlPreFilterService>(),
            scope.ServiceProvider.GetRequiredService<IStopWordsRepository>(),
            scope.ServiceProvider.GetRequiredService<IChatService>(),
            logger);

        var scoreResult = await scoringEngine.ScoreAsync(
            profileData, images, banThreshold, notifyThreshold, ct);

        // ── Step 6: Persist results ──
        await userRepo.UpdateProfileScanDataAsync(
            user.Id, bio, personalChannelId, channelTitle, channelAbout,
            hasPinnedStories, pinnedStoryCaptions, isScam, isFake, isVerified,
            scoreResult.Score, ct);

        var result = new ProfileScanResult(
            user.Id, bio, personalChannelId, channelTitle, channelAbout,
            hasPinnedStories, pinnedStoryCaptions, isScam, isFake, isVerified,
            scoreResult.Score, scoreResult.Outcome, scoreResult.AiReason, scoreResult.AiSignals);

        // ── Step 7: Take moderation action ──
        if (scoreResult.Outcome == ProfileScanOutcome.Banned && triggeringChat != null)
        {
            await HandleBanAsync(user, triggeringChat, result, scope.ServiceProvider, ct);
        }
        else if (scoreResult.Outcome == ProfileScanOutcome.HeldForReview && triggeringChat != null)
        {
            await CreateProfileScanAlertAsync(user, triggeringChat, result, scope.ServiceProvider, ct);
        }

        return result;
    }

    private async Task HandleBanAsync(
        UserIdentity user,
        ChatIdentity chat,
        ProfileScanResult result,
        IServiceProvider sp,
        CancellationToken ct)
    {
        // Fresh read to avoid duplicate bans
        var userRepo = sp.GetRequiredService<ITelegramUserRepository>();
        if (await userRepo.IsBannedAsync(user.Id, ct))
        {
            logger.LogDebug("Profile scan: {User} already banned, skipping ban action", user.ToLogDebug());
            return;
        }

        var moderationService = sp.GetRequiredService<IBotModerationService>();
        var intent = new BanIntent
        {
            User = user,
            Executor = Actor.ProfileScan,
            Reason = $"Profile scan: score {result.Score:F1}/5.0 — {result.AiReason ?? "rule-based detection"}",
            Chat = chat
        };

        await moderationService.BanUserAsync(intent, ct);

        // Censor explicit profile photo if banned for adult content
        await CensorProfilePhotoAsync(user, ct);

        logger.LogInformation("Profile scan: banned {User} (score {Score})", user.ToLogInfo(), result.Score);
    }

    private async Task CreateProfileScanAlertAsync(
        UserIdentity user,
        ChatIdentity chat,
        ProfileScanResult result,
        IServiceProvider sp,
        CancellationToken ct)
    {
        var reportsRepo = sp.GetRequiredService<IReportsRepository>();

        // Check for existing pending alert for this user in this chat
        if (await reportsRepo.HasPendingProfileScanAlertAsync(user.Id, chat.Id, ct))
        {
            logger.LogDebug("Profile scan: pending alert already exists for {User} in {Chat}",
                user.ToLogDebug(), chat.ToLogDebug());
            return;
        }

        var alert = new ProfileScanAlertRecord
        {
            User = user,
            Chat = chat,
            Score = result.Score,
            Outcome = result.Outcome,
            AiReason = result.AiReason,
            AiSignalsDetected = result.AiSignalsDetected,
            Bio = result.Bio,
            PersonalChannelTitle = result.PersonalChannelTitle,
            HasPinnedStories = result.HasPinnedStories,
            IsScam = result.IsScam,
            IsFake = result.IsFake,
            DetectedAt = DateTimeOffset.UtcNow
        };

        var reportId = await reportsRepo.InsertProfileScanAlertAsync(alert, ct);

        // Schedule admin notification
        var jobTrigger = sp.GetRequiredService<IJobTriggerService>();
        var signals = result.AiSignalsDetected is { Length: > 0 }
            ? string.Join(", ", result.AiSignalsDetected)
            : "rule-based detection";

        var payload = new SendChatNotificationPayload(
            Chat: chat,
            EventType: NotificationEventType.ProfileScanAlert,
            Subject: $"Profile Scan Alert — Score {result.Score:F1}/5.0",
            Message: $"User profile flagged for review.\nReason: {result.AiReason ?? signals}",
            ReportId: reportId,
            ReportedUserId: user.Id,
            ReportType: ReportType.ProfileScanAlert);

        await jobTrigger.TriggerNowAsync("SendChatNotificationJob", payload, ct);

        logger.LogInformation("Profile scan: created alert #{ReportId} for {User} in {Chat} (score {Score})",
            reportId, user.ToLogInfo(), chat.ToLogInfo(), result.Score);
    }

    private async Task CensorProfilePhotoAsync(UserIdentity user, CancellationToken ct)
    {
        var photoPath = Path.Combine("/data", "media", "user_photos", $"{user.Id}.jpg");
        if (!File.Exists(photoPath))
            return;

        try
        {
            using var image = await Image.LoadAsync(photoPath, ct);
            image.Mutate(x => x.GaussianBlur(40));
            await image.SaveAsJpegAsync(photoPath, ct);
            logger.LogInformation("Profile scan: censored profile photo for banned {User}", user.ToLogInfo());
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Profile scan: failed to censor profile photo for {User}", user.ToLogDebug());
        }
    }

    private static async Task<List<ImageInput>> CollectImagesAsync(
        string? existingPhotoPath,
        CancellationToken ct)
    {
        var images = new List<ImageInput>();

        // Profile photo from disk (already saved by photo service)
        if (!string.IsNullOrEmpty(existingPhotoPath))
        {
            var fullPath = Path.Combine("/data", "media", existingPhotoPath);
            if (File.Exists(fullPath))
            {
                try
                {
                    var bytes = await File.ReadAllBytesAsync(fullPath, ct);
                    images.Add(new ImageInput(bytes, "image/jpeg"));
                }
                catch (Exception)
                {
                    // Non-fatal — continue without photo
                }
            }
        }

        return images;
    }

    private async Task<ProfileScanOutcome> DetermineOutcomeAsync(
        decimal score,
        ChatIdentity? chat,
        IServiceProvider sp,
        CancellationToken ct)
    {
        var configService = sp.GetRequiredService<IConfigService>();
        var welcomeConfig = await configService.GetEffectiveAsync<WelcomeConfig>(ConfigType.Welcome, chat?.Id ?? 0);
        var profileScanConfig = welcomeConfig?.JoinSecurity?.ProfileScan;
        var banThreshold = profileScanConfig?.BanThreshold ?? 4.0m;
        var notifyThreshold = profileScanConfig?.NotifyThreshold ?? 2.0m;

        return score >= banThreshold
            ? ProfileScanOutcome.Banned
            : score >= notifyThreshold
                ? ProfileScanOutcome.HeldForReview
                : ProfileScanOutcome.Clean;
    }

    private static ProfileScanResult EmptyResult(long userId) =>
        new(userId, null, null, null, null, false, null, false, false, false,
            0.0m, ProfileScanOutcome.Clean, null, null);
}
