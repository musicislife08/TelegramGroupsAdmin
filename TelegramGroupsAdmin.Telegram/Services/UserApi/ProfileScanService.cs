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
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Models;
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

        // Enrich identity from DB if caller only provided a bare ID (e.g., rescan job)
        if (existingUser != null && user.FirstName is null && user.LastName is null && user.Username is null)
            user = UserIdentity.From(existingUser);

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
            return EmptyResult(user.Id, "No User API session available. Connect a session in Settings.");
        }

        // ── Step 1: Resolve user (Telegram requires access_hash, not bare IDs) ──
        var resolvedUser = await ResolveUserAsync(client, user.Id, existingUser, ct);
        if (resolvedUser == null)
        {
            logger.LogWarning("Could not resolve {User} — marking as excluded from future rescans", user.ToLogDebug());
            await userRepo.SetProfileScanExcludedAsync(user.Id, true, ct);
            return EmptyResult(user.Id, "User could not be resolved — they may have deleted their Telegram account.");
        }

        // ── Step 2: Fetch full user info using resolved InputUser ──
        Users_UserFull fullUser;
        try
        {
            fullUser = await client.Users_GetFullUser(resolvedUser);
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

        // ── Step 3: Resolve personal channel ──
        string? channelTitle = null;
        string? channelAbout = null;
        Channel? personalChannel = null;
        if (personalChannelId is > 0)
        {
            // The personal channel is included in fullUser.chats with a valid access_hash.
            // Using new InputChannel(id, 0) fails with CHANNEL_INVALID because access_hash is required.
            personalChannel = fullUser.chats.TryGetValue(personalChannelId.Value, out var chatBase)
                ? chatBase as Channel
                : null;

            if (personalChannel != null)
            {
                channelTitle = personalChannel.title;
                try
                {
                    // Implicit Channel → InputChannel conversion carries the access_hash
                    var channelFull = await client.Channels_GetFullChannel(personalChannel);
                    if (channelFull.full_chat is ChannelFull cf)
                        channelAbout = cf.about;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to fetch full channel info for {ChannelId} for {User}",
                        personalChannelId, user.ToLogDebug());
                }
            }
        }

        // ── Step 4: Fetch pinned stories ──
        string? pinnedStoryCaptions = null;
        int storyCount = 0;
        List<string>? storyCaptions = null;
        StoryItem[]? storyItems = null;
        if (hasPinnedStories)
        {
            try
            {
                var peerStories = await client.Stories_GetPeerStories(resolvedUser);
                var stories = peerStories.stories?.stories;
                if (stories is { Length: > 0 })
                {
                    storyCount = stories.Length;
                    storyItems = stories.OfType<StoryItem>().ToArray();
                    storyCaptions = storyItems
                        .Where(s => !string.IsNullOrEmpty(s.caption))
                        .Select(s => s.caption)
                        .ToList();
                    if (storyCaptions.Count > 0)
                        pinnedStoryCaptions = string.Join("\n", storyCaptions);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fetch pinned stories for {User}", user.ToLogDebug());
            }
        }

        // ── Step 5: Collect images for AI vision ──
        var imageResult = await CollectImagesAsync(client, tlUser, personalChannel, storyItems, ct);

        // ── Step 6: Run scoring engine ──
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
            profileData, imageResult.Images, imageResult.Labels, banThreshold, notifyThreshold, ct);

        // ── Step 7: Persist results ──
        await userRepo.UpdateProfileScanDataAsync(
            user.Id, bio, personalChannelId, channelTitle, channelAbout,
            hasPinnedStories, pinnedStoryCaptions, isScam, isFake, isVerified,
            scoreResult.Score, ct);

        // Clear exclusion flag on successful scan (user is accessible again)
        if (existingUser?.ProfileScanExcluded == true)
            await userRepo.SetProfileScanExcludedAsync(user.Id, false, ct);

        // Persist scan result history
        var scanResultsRepo = scope.ServiceProvider.GetRequiredService<IProfileScanResultsRepository>();
        await scanResultsRepo.InsertAsync(new ProfileScanResultRecord(
            Id: 0,
            UserId: user.Id,
            ScannedAt: DateTimeOffset.UtcNow,
            Score: scoreResult.Score,
            Outcome: scoreResult.Outcome,
            RuleScore: scoreResult.RuleScore,
            AiScore: scoreResult.AiScore,
            AiConfidence: scoreResult.AiConfidence,
            AiReason: scoreResult.AiReason,
            AiSignals: scoreResult.AiSignals is { Length: > 0 }
                ? string.Join(", ", scoreResult.AiSignals) : null), ct);

        var result = new ProfileScanResult(
            user.Id, bio, personalChannelId, channelTitle, channelAbout,
            hasPinnedStories, pinnedStoryCaptions, isScam, isFake, isVerified,
            scoreResult.Score, scoreResult.Outcome, scoreResult.AiReason, scoreResult.AiSignals);

        // ── Step 8: Take moderation action ──
        if (scoreResult.Outcome == ProfileScanOutcome.Banned)
            await HandleBanAsync(user, triggeringChat, result, scope.ServiceProvider, ct);
        else if (scoreResult.Outcome == ProfileScanOutcome.HeldForReview)
            await CreateProfileScanAlertAsync(user, triggeringChat, result, scope.ServiceProvider, ct);

        return result;
    }

    /// <summary>
    /// Resolve a user's access_hash via Telegram API so we can call Users_GetFullUser.
    /// Telegram requires access_hash for user lookups — bare IDs return USER_ID_INVALID.
    /// Resolution chain: username (exact) → name search (match by ID) → not resolvable.
    /// </summary>
    private async Task<TL.User?> ResolveUserAsync(
        IWTelegramApiClient client,
        long userId,
        Models.TelegramUser? existingUser,
        CancellationToken ct)
    {
        // Strategy 1: Resolve by username (exact global lookup, most reliable)
        var username = existingUser?.Username;
        if (!string.IsNullOrEmpty(username))
        {
            try
            {
                var resolved = await client.Contacts_ResolveUsername(username);
                var resolvedUser = resolved.User;
                if (resolvedUser?.id == userId)
                {
                    logger.LogDebug("Resolved {UserId} via username @{Username}", userId, username);
                    return resolvedUser;
                }
            }
            catch (RpcException ex) when (ex.Code == 400)
            {
                logger.LogDebug("Username @{Username} no longer valid for {UserId}", username, userId);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to resolve username @{Username} for {UserId}", username, userId);
            }
        }

        // Strategy 2: Search by name (global search, match result by user ID)
        var searchQuery = BuildSearchQuery(existingUser);
        if (!string.IsNullOrEmpty(searchQuery))
        {
            try
            {
                var found = await client.Contacts_Search(searchQuery, 50);
                if (found.users.TryGetValue(userId, out var matchedUser))
                {
                    logger.LogDebug("Resolved {UserId} via name search for \"{Query}\"", userId, searchQuery);
                    return matchedUser;
                }

                logger.LogDebug("Name search for \"{Query}\" returned {Count} users but none matched {UserId}",
                    searchQuery, found.users.Count, userId);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to search for \"{Query}\" to resolve {UserId}", searchQuery, userId);
            }
        }

        return null;
    }

    private static string? BuildSearchQuery(Models.TelegramUser? user)
    {
        if (user == null) return null;

        // Prefer full name for search (more specific than first name alone)
        var name = $"{user.FirstName} {user.LastName}".Trim();
        if (!string.IsNullOrEmpty(name))
            return name;

        // Fall back to username without @
        return user.Username;
    }

    private async Task HandleBanAsync(
        UserIdentity user,
        ChatIdentity? chat,
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
        ChatIdentity? chat,
        ProfileScanResult result,
        IServiceProvider sp,
        CancellationToken ct)
    {
        var reportsRepo = sp.GetRequiredService<IReportsRepository>();

        // Dedup: null chatId = global dedup (any pending alert for this user in any chat)
        if (await reportsRepo.HasPendingProfileScanAlertAsync(user.Id, chat?.Id, ct))
        {
            logger.LogDebug("Profile scan: pending alert already exists for {User}",
                user.ToLogDebug());
            return;
        }

        // Use sentinel chat_id=0 when user has no active chat (left all groups / deleted account)
        var alertChat = chat ?? ChatIdentity.FromId(0);

        var alert = new ProfileScanAlertRecord
        {
            User = user,
            Chat = alertChat,
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

        // Schedule admin notification only when we have a real chat (admins can't be looked up for sentinel chat_id=0)
        if (chat != null)
        {
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
        else
        {
            logger.LogInformation("Profile scan: created background alert #{ReportId} for {User} (score {Score}, no chat for notification)",
                reportId, user.ToLogInfo(), result.Score);
        }
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

    private record ImageCollectionResult(List<ImageInput> Images, string? Labels);

    private const int MaxStoryImages = 3;
    private const int VisionMaxDimension = 512;

    private async Task<ImageCollectionResult> CollectImagesAsync(
        IWTelegramApiClient client,
        TL.User? tlUser,
        Channel? personalChannel,
        StoryItem[]? stories,
        CancellationToken ct)
    {
        var images = new List<ImageInput>();
        var labels = new List<string>();

        // 1. Profile photo via WTelegram
        if (tlUser?.photo is UserProfilePhoto)
        {
            try
            {
                using var ms = new MemoryStream();
                var fileType = await client.DownloadProfilePhotoAsync(tlUser, ms, big: false);
                var resized = await ResizeForVisionAsync(ms.ToArray());
                images.Add(new ImageInput(resized, ToMimeType(fileType)));
                labels.Add("profile photo");
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to download profile photo for vision");
            }
        }

        // 2. Personal channel photo
        if (personalChannel?.photo is ChatPhoto)
        {
            try
            {
                using var ms = new MemoryStream();
                var fileType = await client.DownloadProfilePhotoAsync(personalChannel, ms, big: false);
                var resized = await ResizeForVisionAsync(ms.ToArray());
                images.Add(new ImageInput(resized, ToMimeType(fileType)));
                labels.Add("personal channel photo");
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to download channel photo for vision");
            }
        }

        // 3. Pinned story images (up to MaxStoryImages)
        if (stories is { Length: > 0 })
        {
            var storyPhotoCount = 0;
            foreach (var story in stories)
            {
                if (storyPhotoCount >= MaxStoryImages) break;
                if (story.media is not MessageMediaPhoto { photo: Photo photo }) continue;

                try
                {
                    using var ms = new MemoryStream();
                    var fileType = await client.DownloadFileAsync(photo, ms);
                    var resized = await ResizeForVisionAsync(ms.ToArray());
                    images.Add(new ImageInput(resized, ToMimeType(fileType)));
                    labels.Add("pinned story");
                    storyPhotoCount++;
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Failed to download story image for vision");
                }
            }
        }

        logger.LogDebug("Collected {ImageCount} images for vision analysis: {Labels}",
            images.Count, string.Join(", ", labels));

        var labelString = labels.Count > 0
            ? string.Join(", ", labels.Select((l, i) => $"Image {i + 1}: {l}"))
            : null;

        return new ImageCollectionResult(images, labelString);
    }

    private static async Task<byte[]> ResizeForVisionAsync(byte[] imageBytes, int maxDimension = VisionMaxDimension)
    {
        using var image = Image.Load(imageBytes);

        if (image.Width <= maxDimension && image.Height <= maxDimension)
            return imageBytes;

        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(maxDimension, maxDimension),
            Mode = ResizeMode.Max // Scale largest dimension, preserve aspect ratio
        }));

        using var output = new MemoryStream();
        await image.SaveAsJpegAsync(output, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = 85 });
        return output.ToArray();
    }

    private static string ToMimeType(Storage_FileType fileType) => fileType switch
    {
        Storage_FileType.jpeg => "image/jpeg",
        Storage_FileType.png => "image/png",
        Storage_FileType.webp => "image/webp",
        Storage_FileType.gif => "image/gif",
        _ => "image/jpeg"
    };

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

    private static ProfileScanResult EmptyResult(long userId, string? skipReason = null) =>
        new(userId, null, null, null, null, false, null, false, false, false,
            0.0m, ProfileScanOutcome.Clean, null, null, skipReason);
}
