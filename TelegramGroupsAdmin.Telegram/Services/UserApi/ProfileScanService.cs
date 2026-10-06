using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IO;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Imaging;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.AI.Services;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using System.Collections.Concurrent;
using System.Diagnostics;
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Identity;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using TL;
using TelegramGroupsAdmin.Configuration.Services;

namespace TelegramGroupsAdmin.Telegram.Services.UserApi;

/// <summary>
/// Central orchestrator for profile scanning. Calls WTelegram API, runs scoring,
/// persists results, and takes moderation action (ban/report).
/// Singleton — uses IServiceScopeFactory for scoped dependency access.
/// </summary>
public sealed class ProfileScanService(
    ITelegramSessionManager sessionManager,
    IServiceScopeFactory scopeFactory,
    PipelineMetrics pipelineMetrics,
    RecyclableMemoryStreamManager streamManager,
    IImageProcessor imageProcessor,
    ILogger<ProfileScanService> logger) : IProfileScanService
{
    /// <summary>
    /// Skip re-scanning if profile was scanned within this window.
    /// Multi-chat dedup: if user joins two chats simultaneously, only one scan runs.
    /// </summary>
    private static readonly TimeSpan ScanFreshnessWindow = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Maximum time for the core scan (Telegram API calls, file downloads, AI scoring).
    /// Prevents hung DC connections from blocking the welcome flow indefinitely.
    /// </summary>
    internal TimeSpan ScanTimeout { get; init; } = TimeSpan.FromSeconds(45);

    // One scan per (user, force), shared by concurrent callers. The 60s freshness window alone
    // cannot do this: profile_scanned_at is written only when a scan finishes. Chat is deliberately
    // not in the key: bot updates are processed one at a time and join scans run inline, so two
    // chats' joins never overlap (the second reaches the 60s dedup path, which recomputes the
    // outcome with its own chat's thresholds). Sharing only matters when an off-loop source
    // (rescan job, manual rescan) overlaps a join or rename scan; there the first caller's chat
    // decides, which is acceptable. Force is in the key so a forced rescan never joins a
    // cache-eligible run (or vice versa).
    private readonly ConcurrentDictionary<(long UserId, bool ForceRescan), Lazy<Task<ProfileScanResult>>> _inFlight = new();

    public async Task<ProfileScanResult> ScanUserProfileAsync(
        UserIdentity user,
        ChatIdentity? triggeringChat,
        CancellationToken ct,
        bool forceRescan = false)
    {
        // The shared run uses CancellationToken.None (ScanTimeout bounds it) so the first caller's
        // cancellation cannot cancel it for others; each caller's ct only stops its own wait.
        // Removal is tied to the run's completion, not to any caller, so a cancelled caller cannot
        // evict a still-running scan and let a later caller start a duplicate.
        var key = (user.Id, forceRescan);
        Lazy<Task<ProfileScanResult>> candidate = null!;
        candidate = new Lazy<Task<ProfileScanResult>>(
            () => RunAndRemoveAsync(key, candidate, user, triggeringChat, forceRescan));
        var lazy = _inFlight.GetOrAdd(key, candidate);
        return await lazy.Value.WaitAsync(ct);
    }

    private async Task<ProfileScanResult> RunAndRemoveAsync(
        (long UserId, bool ForceRescan) key,
        Lazy<Task<ProfileScanResult>> self,
        UserIdentity user,
        ChatIdentity? triggeringChat,
        bool forceRescan)
    {
        try
        {
            return await ScanOnceAsync(user, triggeringChat, forceRescan, CancellationToken.None);
        }
        finally
        {
            // Pair removal: only ever removes this run's own entry, never a newer one.
            _inFlight.TryRemove(new KeyValuePair<(long, bool), Lazy<Task<ProfileScanResult>>>(key, self));
        }
    }

    private async Task<ProfileScanResult> ScanOnceAsync(
        UserIdentity user,
        ChatIdentity? triggeringChat,
        bool forceRescan,
        CancellationToken ct)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        var scanSource = triggeringChat is not null ? "welcome" : "rescan";

        await using var scope = scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var userRepo = sp.GetRequiredService<ITelegramUserRepository>();

        // ── Multi-chat dedup: skip if recently scanned ──
        var existingUser = await userRepo.GetByTelegramIdAsync(user.Id, ct);

        // Enrich identity if caller only provided a bare ID (e.g., rescan job)
        if (user.FirstName is null && user.LastName is null && user.Username is null)
            user = await sp.GetRequiredService<IUserIdentityService>().ResolveAsync(user.Id, ct);

        // Both reuse paths (this freshness window and the unchanged-profile check) are skipped on a
        // forced rescan, when a rename was recorded after the last scan, and when the last scan only
        // read the name. Joins and admin refresh record a rename without scanning, so the stored names
        // already match the live ones and only username_history still shows the change. A name-only
        // verdict is never reused: the next scan that can read the profile replaces it.
        var skipReuse = forceRescan
            || await RenamedSinceLastScanAsync(existingUser, sp, ct)
            || await LatestScanWasNameOnlyAsync(existingUser, sp, ct);

        if (!skipReuse && existingUser?.ProfileScannedAt is { } lastScan
            && DateTimeOffset.UtcNow - lastScan < ScanFreshnessWindow
            && existingUser.ProfileScanScore.HasValue)
        {
            logger.LogDebug("Profile scan for {User}: recently scanned ({LastScan}), reusing cached score {Score}",
                user.ToLogDebug(), lastScan, existingUser.ProfileScanScore);

            pipelineMetrics.RecordProfileScanSkipped("dedup");
            var cachedOutcome = await DetermineOutcomeAsync(existingUser.ProfileScanScore.Value, triggeringChat, sp, ct);
            return new ProfileScanResult(
                TelegramUserId: user.Id,
                Bio: existingUser.Bio,
                PersonalChannelId: existingUser.PersonalChannelId,
                PersonalChannelTitle: existingUser.PersonalChannelTitle,
                PersonalChannelAbout: existingUser.PersonalChannelAbout,
                HasPinnedStories: existingUser.HasPinnedStories,
                PinnedStoryCaptions: existingUser.PinnedStoryCaptions,
                IsScam: existingUser.IsScam,
                IsFake: existingUser.IsFake,
                IsVerified: existingUser.IsVerified,
                Score: existingUser.ProfileScanScore.Value,
                Outcome: cachedOutcome,
                AiReason: null,
                AiSignalsDetected: null,
                ExplicitDisplayText: false);
        }

        // ── Get User API client (prefer one with access to the triggering chat) ──
        var client = triggeringChat is not null
            ? await sessionManager.GetClientForChatAsync(triggeringChat.Id, ct)
            : await sessionManager.GetAnyClientAsync(ct);
        if (client == null)
        {
            logger.LogWarning("No User API client available for profile scan of {User}", user.ToLogDebug());
            pipelineMetrics.RecordProfileScanSkipped("no_session");
            return await FallBackToNameOnlyAsync(
                EmptyResult(user.Id, "No User API session available. Connect a session in Settings."),
                user, existingUser, triggeringChat, sp, ct);
        }

        // Top-level guard: WTelegram API calls don't accept CancellationToken, so a hung DC
        // connection (e.g., file download from DC -4) blocks indefinitely. Task.WhenAny races
        // the scan against a timeout — if the timeout wins, we abandon the scan and return
        // gracefully so the welcome flow continues.
        //
        // The scan task gets its own scope (via ScanWithOwnedScopeAsync) so that if the timeout
        // fires and this method returns, the abandoned task's scoped services stay alive until
        // the task completes or faults — preventing ObjectDisposedException on DbContexts.
        ProfileScanResult result;
        try
        {
            var scanTask = ScanWithOwnedScopeAsync(client, user, existingUser, triggeringChat, skipReuse, ct);

            var completedTask = await Task.WhenAny(scanTask, Task.Delay(ScanTimeout, CancellationToken.None));

            if (completedTask != scanTask)
            {
                logger.LogWarning(
                    "Profile scan timed out after {Timeout}s for {User} (WTelegram call hung, likely DC connection issue)",
                    ScanTimeout.TotalSeconds, user.ToLogDebug());

                // Observe the abandoned task to prevent UnobservedTaskException and log failures
                _ = scanTask.ContinueWith(
                    t => logger.LogDebug(t.Exception?.GetBaseException(),
                        "Abandoned profile scan for {UserId} faulted after timeout", user.Id),
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);

                pipelineMetrics.RecordProfileScanTimeout();
                result = EmptyResult(user.Id, $"Scan timed out after {ScanTimeout.TotalSeconds}s");
            }
            else
            {
                result = await scanTask;
                pipelineMetrics.RecordProfileScan(
                    OutcomeToTag(result.Outcome), scanSource,
                    Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
            }
        }
        catch (TelegramFloodWaitException ex)
        {
            logger.LogWarning("Rate limited during scan of {User} — {Message}, falling back to a name-only scan",
                user.ToLogDebug(), ex.Message);
            result = EmptyResult(user.Id, ex.Message);
        }

        // A skip reason here means the profile could not be read (user not resolvable, full profile
        // not fetched, timeout, FLOOD_WAIT). A scan that read the profile never carries one.
        // On the timeout path the name-only AI call runs after the ScanTimeout race, so ScanTimeout
        // does not bound it; the AI client's own HTTP timeout does.
        return result.SkipReason is null
            ? result
            : await FallBackToNameOnlyAsync(result, user, existingUser, triggeringChat, sp, ct);
    }

    /// <summary>
    /// Runs the core scan with its own DI scope. The scope stays alive for the task's full
    /// lifetime, even if the caller abandons the task due to timeout.
    /// </summary>
    private async Task<ProfileScanResult> ScanWithOwnedScopeAsync(
        IWTelegramApiClient client,
        UserIdentity user,
        Models.TelegramUser? existingUser,
        ChatIdentity? triggeringChat,
        bool skipReuse,
        CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var userRepo = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();

        return await ScanUserProfileCoreAsync(client, user, existingUser, triggeringChat, skipReuse,
            userRepo, scope.ServiceProvider, ct);
    }

    private async Task<ProfileScanResult> ScanUserProfileCoreAsync(
        IWTelegramApiClient client,
        UserIdentity user,
        Models.TelegramUser? existingUser,
        ChatIdentity? triggeringChat,
        bool skipReuse,
        ITelegramUserRepository userRepo,
        IServiceProvider sp,
        CancellationToken ct)
    {
        // ── Step 1: Resolve user (Telegram requires access_hash, not bare IDs) ──
        var resolvedUser = await ResolveUserAsync(client, user.Id, existingUser, triggeringChat, ct);
        if (resolvedUser == null)
        {
            logger.LogWarning("Could not resolve {User}", user.ToLogDebug());
            pipelineMetrics.RecordProfileScanSkipped("unresolvable");
            return EmptyResult(user.Id, "User could not be resolved — they may have deleted their Telegram account.");
        }

        // ── Step 2: Fetch full user info using resolved InputUser ──
        Users_UserFull fullUser;
        try
        {
            fullUser = await client.Users_GetFullUser(resolvedUser);
        }
        catch (TelegramFloodWaitException) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get full user info for {User}", user.ToLogDebug());
            return EmptyResult(user.Id, "Could not fetch the user's full profile.");
        }

        var userInfo = fullUser.full_user;
        var tlUser = fullUser.users.Values.OfType<TL.User>().FirstOrDefault(u => u.id == user.Id);

        // Record the live names. existingUser is the snapshot read before this point (a detached
        // object, not re-read), so HasProfileChanged below still compares the live names against
        // the pre-observe row and a rename discovered here still gets a full rescore. Source UserApiScan:
        // ObserveAsync records only, so the scan never triggers itself. ObserveAsync never throws except on cancellation.
        if (tlUser is not null)
        {
            await sp.GetRequiredService<IUserIdentityService>().ObserveAsync(
                new ObservedUser(tlUser.id, tlUser.first_name, tlUser.last_name, tlUser.MainUsername,
                    tlUser.IsBot, ObservationSource.UserApiScan, DateTimeOffset.UtcNow),
                new ProfileChangeContext(triggeringChat, null),
                ct);
        }

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
                catch (TelegramFloodWaitException) { throw; }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to fetch full channel info for {ChannelId} for {User}",
                        personalChannelId, user.ToLogDebug());
                }
            }
        }

        // ── Step 4: Collect active + pinned stories, resolve min stories for captions ──
        string? pinnedStoryCaptions = null;
        int storyCount = 0;
        List<string>? storyCaptions = null;
        StoryItem[]? storyItems = null;

        var hasActiveStories = userInfo?.flags.HasFlag(UserFull.Flags.has_stories) == true;

        // Merge stories from both sources, dedupe by story ID
        var allStoryItems = new Dictionary<int, StoryItem>();

        // 4a. Active stories from UserFull (already available — no extra API call)
        if (hasActiveStories && userInfo?.stories?.stories is { Length: > 0 } activeStories)
        {
            foreach (var s in activeStories.OfType<StoryItem>())
                allStoryItems.TryAdd(s.id, s);
        }

        // 4b. Pinned stories (separate API call)
        if (hasPinnedStories)
        {
            try
            {
                var pinned = await client.Stories_GetPinnedStories(resolvedUser);
                if (pinned.stories is { Length: > 0 } pinnedStories)
                {
                    foreach (var s in pinnedStories.OfType<StoryItem>())
                        allStoryItems.TryAdd(s.id, s);
                }
            }
            catch (TelegramFloodWaitException) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fetch pinned stories for {User}", user.ToLogDebug());
            }
        }

        // 4c. Re-fetch min stories to get full data (captions are omitted on min stories)
        var minStoryIds = allStoryItems.Values
            .Where(s => s.flags.HasFlag(StoryItem.Flags.min))
            .Select(s => s.id)
            .ToArray();

        if (minStoryIds.Length > 0)
        {
            try
            {
                var fullStories = await client.Stories_GetStoriesByID(resolvedUser, minStoryIds);
                if (fullStories.stories is { Length: > 0 })
                {
                    foreach (var s in fullStories.stories.OfType<StoryItem>())
                        allStoryItems[s.id] = s; // Replace min version with full version
                }
            }
            catch (TelegramFloodWaitException) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to resolve min stories for {User}", user.ToLogDebug());
            }
        }

        if (allStoryItems.Count > 0)
        {
            storyCount = allStoryItems.Count;
            storyItems = [.. allStoryItems.Values];
            storyCaptions = storyItems
                .Where(s => !string.IsNullOrEmpty(s.caption))
                .Select(s => s.caption)
                .ToList();
            if (storyCaptions.Count > 0)
                pinnedStoryCaptions = string.Join("\n", storyCaptions);
        }

        // ── Diff check: skip expensive Steps 5-8 if profile is unchanged ──
        var profilePhotoId = (tlUser?.photo as UserProfilePhoto)?.photo_id;
        var channelPhotoId = (personalChannel?.photo as ChatPhoto)?.photo_id;
        var pinnedStoryIdString = storyItems is { Length: > 0 }
            ? string.Join(",", storyItems.Select(s => s.id).Order())
            : null;

        // skipReuse (forced, or renamed since the last scan) skips this reuse too: the rename is
        // already stored when the scan runs, so the diff sees no change.
        if (!skipReuse && existingUser?.ProfileScannedAt != null && existingUser.ProfileScanScore.HasValue
            && !HasProfileChanged(existingUser, tlUser, bio, personalChannelId, channelTitle, channelAbout,
                hasPinnedStories, pinnedStoryCaptions, isScam, isFake, isVerified,
                profilePhotoId, channelPhotoId, pinnedStoryIdString))
        {
            logger.LogInformation("Profile unchanged for {User}, skipping AI scoring (last score {Score})",
                user.ToLogInfo(), existingUser.ProfileScanScore);

            await userRepo.UpdateProfileScannedAtAsync(user.Id, ct);

            var cachedOutcome = await DetermineOutcomeAsync(existingUser.ProfileScanScore.Value, triggeringChat, sp, ct);
            return new ProfileScanResult(
                TelegramUserId: user.Id,
                Bio: bio,
                PersonalChannelId: personalChannelId,
                PersonalChannelTitle: channelTitle,
                PersonalChannelAbout: channelAbout,
                HasPinnedStories: hasPinnedStories,
                PinnedStoryCaptions: pinnedStoryCaptions,
                IsScam: isScam,
                IsFake: isFake,
                IsVerified: isVerified,
                Score: existingUser.ProfileScanScore.Value,
                Outcome: cachedOutcome,
                AiReason: null,
                AiSignalsDetected: null,
                ExplicitDisplayText: false);
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

        var configService = sp.GetRequiredService<IConfigService>();
        var welcomeConfig = await configService.GetEffectiveWelcomeAsync(chat.Id);
        var profileScanConfig = welcomeConfig?.JoinSecurity?.ProfileScan;
        var banThreshold = profileScanConfig?.BanThreshold ?? ProfileScanConfig.DefaultBanThreshold;
        var notifyThreshold = profileScanConfig?.NotifyThreshold ?? ProfileScanConfig.DefaultNotifyThreshold;

        var scoringEngine = sp.GetRequiredService<IProfileScoringEngine>();

        var scoreResult = await scoringEngine.ScoreAsync(
            profileData, imageResult.Images, imageResult.Labels, banThreshold, notifyThreshold, cancellationToken: ct);

        // ── Step 7: Persist results ──
        await userRepo.UpdateProfileScanDataAsync(
            user.Id, bio, personalChannelId, channelTitle, channelAbout,
            hasPinnedStories, pinnedStoryCaptions, isScam, isFake, isVerified,
            scoreResult.Score, profilePhotoId, channelPhotoId, pinnedStoryIdString, ct);

        await PersistScanResultAsync(user.Id, scoreResult, ProfileScanSource.FullScan, sp, ct);

        var result = new ProfileScanResult(
            TelegramUserId: user.Id,
            Bio: bio,
            PersonalChannelId: personalChannelId,
            PersonalChannelTitle: channelTitle,
            PersonalChannelAbout: channelAbout,
            HasPinnedStories: hasPinnedStories,
            PinnedStoryCaptions: pinnedStoryCaptions,
            IsScam: isScam,
            IsFake: isFake,
            IsVerified: isVerified,
            Score: scoreResult.Score,
            Outcome: scoreResult.Outcome,
            AiReason: scoreResult.AiReason,
            AiSignalsDetected: scoreResult.AiSignals,
            ContainsNudity: scoreResult.ContainsNudity,
            ExplicitDisplayText: scoreResult.ExplicitDisplayText,
            PromotionalDisplayText: scoreResult.PromotionalDisplayText);

        // ── Step 8: Take moderation action ──
        await ActOnOutcomeAsync(user, triggeringChat, result, sp, ct);

        return result;
    }

    /// <summary>Writes the scan history row (both scan sources) and counts explicit names.</summary>
    private async Task PersistScanResultAsync(
        long userId, ScoringResult scoreResult, ProfileScanSource source, IServiceProvider sp, CancellationToken ct)
    {
        await sp.GetRequiredService<IProfileScanResultsRepository>().InsertAsync(new ProfileScanResultRecord(
            Id: 0,
            UserId: userId,
            ScannedAt: DateTimeOffset.UtcNow,
            Score: scoreResult.Score,
            Outcome: scoreResult.Outcome,
            RuleScore: scoreResult.RuleScore,
            AiScore: scoreResult.AiScore,
            AiReason: scoreResult.AiReason,
            AiSignals: scoreResult.AiSignals is { Length: > 0 } ? string.Join(", ", scoreResult.AiSignals) : null,
            ExplicitDisplayText: scoreResult.ExplicitDisplayText,
            PromotionalDisplayText: scoreResult.PromotionalDisplayText,
            Source: source), cancellationToken: ct);

        if (scoreResult.ExplicitDisplayText)
            pipelineMetrics.RecordExplicitUsernameDetection(OutcomeToTag(scoreResult.Outcome));
    }

    /// <summary>
    /// Bans or raises a review alert for a scored result. Re-resolves the identity first: the
    /// caller's copy predates this scan's row, so bot-written text would otherwise miss its verdict.
    /// </summary>
    private async Task ActOnOutcomeAsync(
        UserIdentity user, ChatIdentity? triggeringChat, ProfileScanResult result, IServiceProvider sp, CancellationToken ct)
    {
        if (result.Outcome is not (ProfileScanOutcome.Banned or ProfileScanOutcome.HeldForReview))
            return;

        var scannedUser = await sp.GetRequiredService<IUserIdentityService>().ResolveAsync(user.Id, ct);
        if (result.Outcome == ProfileScanOutcome.Banned)
            await HandleBanAsync(scannedUser, triggeringChat, result, sp, ct);
        else
            await CreateProfileScanAlertAsync(scannedUser, triggeringChat, result, sp, ct);
    }

    /// <summary>
    /// The full scan could not read the profile: score the name alone so the user is still filtered,
    /// store it as a NameOnly row and act on it like a scan. Every scan path falls back. When there is
    /// no name, no stored user, a bot, or no verdict, return the skip.
    /// </summary>
    private async Task<ProfileScanResult> FallBackToNameOnlyAsync(
        ProfileScanResult skipped,
        UserIdentity user,
        Models.TelegramUser? existingUser,
        ChatIdentity? triggeringChat,
        IServiceProvider sp,
        CancellationToken ct)
    {
        if (existingUser is null
            || existingUser.IsBot
            || (user.FirstName is null && user.LastName is null && user.Username is null))
        {
            return skipped;
        }

        var configService = sp.GetRequiredService<IConfigService>();
        var profileScanConfig = (await configService.GetEffectiveWelcomeAsync(triggeringChat?.Id ?? 0, ct))?.JoinSecurity?.ProfileScan;
        var nameOnlyBanThreshold = profileScanConfig?.NameOnlyBanThreshold ?? ProfileScanConfig.DefaultNameOnlyBanThreshold;
        var notifyThreshold = profileScanConfig?.NotifyThreshold ?? ProfileScanConfig.DefaultNotifyThreshold;

        var scoreResult = await sp.GetRequiredService<IProfileScoringEngine>()
            .ScoreNameOnlyAsync(user, nameOnlyBanThreshold, notifyThreshold, ct);
        if (scoreResult is null)
        {
            logger.LogWarning("Name-only profile scan for {User} produced no verdict after: {SkipReason}. Nothing recorded",
                user.ToLogDebug(), skipped.SkipReason);
            return skipped;
        }

        await sp.GetRequiredService<ITelegramUserRepository>().UpdateProfileScanScoreAsync(user.Id, scoreResult.Score, ct);
        await PersistScanResultAsync(user.Id, scoreResult, ProfileScanSource.NameOnly, sp, ct);

        var result = EmptyResult(user.Id) with
        {
            Score = scoreResult.Score,
            Outcome = scoreResult.Outcome,
            AiReason = scoreResult.AiReason,
            AiSignalsDetected = scoreResult.AiSignals,
            ExplicitDisplayText = scoreResult.ExplicitDisplayText,
            PromotionalDisplayText = scoreResult.PromotionalDisplayText,
            Source = ProfileScanSource.NameOnly
        };

        logger.LogInformation("Profile scan for {User} could not read the profile ({SkipReason}); name-only scan scored {Score} ({Outcome})",
            user.ToLogInfo(), skipped.SkipReason, result.Score, result.Outcome);

        await ActOnOutcomeAsync(user, triggeringChat, result, sp, ct);
        return result;
    }

    /// <summary>
    /// Whether the user's latest scan only read the name. Only asked when a reuse path could apply.
    /// </summary>
    private static async Task<bool> LatestScanWasNameOnlyAsync(
        Models.TelegramUser? existingUser, IServiceProvider sp, CancellationToken ct)
    {
        if (existingUser?.ProfileScannedAt is null || !existingUser.ProfileScanScore.HasValue)
            return false;
        return await sp.GetRequiredService<IProfileScanResultsRepository>()
            .GetLatestSourceAsync(existingUser.TelegramUserId, ct) == ProfileScanSource.NameOnly;
    }

    /// <summary>
    /// Resolve a user's access_hash via Telegram API so we can call Users_GetFullUser.
    /// Telegram requires access_hash for user lookups — bare IDs return USER_ID_INVALID.
    /// Resolution chain: group participant (scoped) → username (exact) → name search (fuzzy) → not resolvable.
    /// </summary>
    private async Task<TL.User?> ResolveUserAsync(
        IWTelegramApiClient client,
        long userId,
        Models.TelegramUser? existingUser,
        ChatIdentity? triggeringChat,
        CancellationToken ct)
    {
        // Strategy 0: Resolve via group participant lookup (most reliable when chat is known).
        // GetClientForChatAsync returns a best-effort preferred client — verify it actually
        // has access before attempting the API call.
        if (triggeringChat is not null
            && client.GetInputPeerForChat(triggeringChat.Id) is InputPeerChannel inputPeerChannel)
        {
            try
            {
                var inputChannel = new InputChannel(inputPeerChannel.channel_id, inputPeerChannel.access_hash);
                // access_hash=0: not officially documented for user API sessions (only for bots per
                // https://core.telegram.org/api/peers), but works in practice because the server
                // resolves the participant from the channel's member list. If Telegram starts
                // rejecting this, the RpcException catch below falls through to Strategy 1.
                var participantResult = await client.Channels_GetParticipant(
                    inputChannel,
                    new InputPeerUser(userId, 0));

                if (participantResult.users.TryGetValue(userId, out var participantUser))
                {
                    logger.LogDebug("Resolved {UserId} via Channels_GetParticipant in chat {ChatId}",
                        userId, triggeringChat.Id);
                    return participantUser;
                }
            }
            catch (RpcException ex) when (ex.Code == 400)
            {
                // USER_NOT_PARTICIPANT, CHANNEL_INVALID, etc. — fall through to other strategies
                logger.LogDebug("Channels_GetParticipant failed for {UserId} in chat {ChatId}: {Error}",
                    userId, triggeringChat.Id, ex.Message);
            }
            catch (TelegramFloodWaitException) { throw; }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to resolve {UserId} via Channels_GetParticipant in chat {ChatId}",
                    userId, triggeringChat.Id);
            }
        }

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
            catch (TelegramFloodWaitException) { throw; }
            catch (OperationCanceledException) { throw; }
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
            catch (TelegramFloodWaitException) { throw; }
            catch (OperationCanceledException) { throw; }
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

        // Censor explicit profile photo only when nudity was detected in images
        if (result.ContainsNudity)
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

        // Send admin notification only when we have a real chat (admins can't be looked up for sentinel chat_id=0)
        if (chat != null)
        {
            var notificationService = sp.GetRequiredService<IAdminNotificationService>();
            var signals = result.AiSignalsDetected is { Length: > 0 }
                ? string.Join(", ", result.AiSignalsDetected)
                : "rule-based detection";

            // Fire-and-forget — notification delivery should not block scan
            _ = notificationService.SendProfileScanAlertAsync(
                chat: chat,
                user: user,
                score: result.Score,
                signals: signals,
                aiReason: result.AiReason,
                reportId: reportId,
                ct: CancellationToken.None);

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

        var tempPath = photoPath + ".censoring";
        try
        {
            // Blur to a temporary file first: the source and destination are the
            // same path, so streaming straight back would truncate the input.
            var dimensions = ReadDimensions(photoPath);
            if (dimensions is null)
            {
                logger.LogWarning("Profile scan: could not decode profile photo for {User}", user.ToLogDebug());
                return;
            }

            // Skia's blur kernel spans roughly 6*sigma; clamp so it fits the image.
            var maxSigma = Math.Min(dimensions.Width, dimensions.Height) / 6f;
            var sigma = Math.Min(40f, maxSigma);

            await using (var source = File.OpenRead(photoPath))
            await using (var target = File.Create(tempPath))
            {
                if (!await imageProcessor.BlurAsync(source, target, sigma, ImageEncoding.Jpeg(85), ct))
                {
                    logger.LogWarning("Profile scan: blur failed for {User}", user.ToLogDebug());
                    return;
                }
            }

            File.Move(tempPath, photoPath, overwrite: true);
            logger.LogInformation("Profile scan: censored profile photo for banned {User}", user.ToLogInfo());
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Profile scan: failed to censor profile photo for {User}", user.ToLogDebug());
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (IOException ex)
                {
                    // Best-effort cleanup: a stray .censoring file is harmless and will
                    // be overwritten by the next censoring attempt at this path.
                    logger.LogDebug(ex, "Could not delete temp censoring file: {TempPath}", tempPath);
                }
            }
        }
    }

    private ImageDimensions? ReadDimensions(string path)
    {
        using var stream = File.OpenRead(path);
        return imageProcessor.ReadDimensions(stream);
    }

    private record ImageCollectionResult(List<ImageInput> Images, string? Labels);

    private const int MaxStoryImages = 4;
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
                using var ms = streamManager.GetStream("ProfileScan.ProfilePhoto");
                var fileType = await client.DownloadProfilePhotoAsync(tlUser, ms, big: false);
                ms.Position = 0;
                var resized = await ResizeForVisionAsync(ms);
                images.Add(new ImageInput(resized, ToMimeType(fileType)));
                labels.Add("profile photo");
            }
            catch (TelegramFloodWaitException) { throw; }
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
                using var ms = streamManager.GetStream("ProfileScan.ChannelPhoto");
                var fileType = await client.DownloadProfilePhotoAsync(personalChannel, ms, big: false);
                ms.Position = 0;
                var resized = await ResizeForVisionAsync(ms);
                images.Add(new ImageInput(resized, ToMimeType(fileType)));
                labels.Add("personal channel photo");
            }
            catch (TelegramFloodWaitException) { throw; }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to download channel photo for vision");
            }
        }

        // 3. Story images — photos + video thumbnails (up to MaxStoryImages)
        if (stories is { Length: > 0 })
        {
            var storyImageCount = 0;
            foreach (var story in stories)
            {
                if (storyImageCount >= MaxStoryImages) break;

                try
                {
                    switch (story.media)
                    {
                        case MessageMediaPhoto { photo: Photo photo }:
                        {
                            using var ms = streamManager.GetStream("ProfileScan.StoryPhoto");
                            var fileType = await client.DownloadFileAsync(photo, ms);
                            ms.Position = 0;
                            var resized = await ResizeForVisionAsync(ms);
                            images.Add(new ImageInput(resized, ToMimeType(fileType)));
                            labels.Add("story photo");
                            storyImageCount++;
                            break;
                        }
                        case MessageMediaDocument { document: Document doc }
                            when doc.mime_type?.StartsWith("video/") == true:
                        {
                            // Download video thumbnail — no full video download needed
                            var thumb = doc.LargestThumbSize;
                            if (thumb == null) continue;

                            using var ms = streamManager.GetStream("ProfileScan.StoryThumb");
                            await client.DownloadFileAsync(doc, ms, thumb);
                            ms.Position = 0;
                            var resized = await ResizeForVisionAsync(ms);
                            images.Add(new ImageInput(resized, "image/jpeg"));
                            labels.Add("story video thumbnail");
                            storyImageCount++;
                            break;
                        }
                    }
                }
                catch (TelegramFloodWaitException) { throw; }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Failed to download story media for vision");
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

    private async Task<byte[]> ResizeForVisionAsync(Stream imageStream, int maxDimension = VisionMaxDimension)
    {
        imageStream.Position = 0;
        var dimensions = imageProcessor.ReadDimensions(imageStream);

        if (dimensions is null || (dimensions.Width <= maxDimension && dimensions.Height <= maxDimension))
        {
            // Already within bounds, or undecodable — hand back the original bytes
            // rather than re-encoding.
            return await ReadAllBytesAsync(imageStream);
        }

        imageStream.Position = 0;
        using var output = new MemoryStream();
        if (!await imageProcessor.ResizeToFitAsync(imageStream, output, maxDimension, ImageEncoding.Jpeg(85)))
        {
            // Dimensions parsed from the header, but the full decode failed (e.g. a
            // truncated download) — fall back to the original bytes rather than
            // sending an empty image to the vision model.
            return await ReadAllBytesAsync(imageStream);
        }

        return output.ToArray();
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream imageStream)
    {
        imageStream.Position = 0;
        using var passthrough = new MemoryStream((int)imageStream.Length);
        await imageStream.CopyToAsync(passthrough);
        return passthrough.ToArray();
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
        var welcomeConfig = await configService.GetEffectiveWelcomeAsync(chat?.Id ?? 0);
        var profileScanConfig = welcomeConfig?.JoinSecurity?.ProfileScan;
        var banThreshold = profileScanConfig?.BanThreshold ?? ProfileScanConfig.DefaultBanThreshold;
        var notifyThreshold = profileScanConfig?.NotifyThreshold ?? ProfileScanConfig.DefaultNotifyThreshold;

        return score >= banThreshold
            ? ProfileScanOutcome.Banned
            : score >= notifyThreshold
                ? ProfileScanOutcome.HeldForReview
                : ProfileScanOutcome.Clean;
    }

    /// <summary>
    /// Whether a rename was recorded after the user's last scan. Only asked when a reuse path could
    /// apply (the user has a stored scan), so a never-scanned user costs no query.
    /// </summary>
    private static async Task<bool> RenamedSinceLastScanAsync(
        Models.TelegramUser? existingUser, IServiceProvider sp, CancellationToken ct)
    {
        if (existingUser?.ProfileScannedAt is not { } lastScan || !existingUser.ProfileScanScore.HasValue)
            return false;
        return await sp.GetRequiredService<IUsernameHistoryRepository>().HasChangeSinceAsync(existingUser.TelegramUserId, lastScan, ct);
    }

    /// <summary>
    /// Compare fetched profile metadata against stored values to detect changes.
    /// Returns true if any field differs — meaning a full rescan (images + AI) is needed.
    /// </summary>
    private static bool HasProfileChanged(
        Models.TelegramUser existing,
        TL.User? tlUser,
        string? bio,
        long? personalChannelId,
        string? channelTitle,
        string? channelAbout,
        bool hasPinnedStories,
        string? pinnedStoryCaptions,
        bool isScam,
        bool isFake,
        bool isVerified,
        long? profilePhotoId,
        long? channelPhotoId,
        string? pinnedStoryIds)
    {
        // Text/flag fields already stored on user
        if (bio != existing.Bio) return true;
        if (personalChannelId != existing.PersonalChannelId) return true;
        if (channelTitle != existing.PersonalChannelTitle) return true;
        if (channelAbout != existing.PersonalChannelAbout) return true;
        if (hasPinnedStories != existing.HasPinnedStories) return true;
        if (pinnedStoryCaptions != existing.PinnedStoryCaptions) return true;
        if (isScam != existing.IsScam) return true;
        if (isFake != existing.IsFake) return true;
        if (isVerified != existing.IsVerified) return true;

        // Name/username changes (stored on user record, not in profile scan columns)
        if (tlUser?.first_name != existing.FirstName) return true;
        if (tlUser?.last_name != existing.LastName) return true;
        if (tlUser?.MainUsername != existing.Username) return true;

        // Telegram ID-based fields (detect image/story changes without downloading)
        if (profilePhotoId != existing.ProfilePhotoId) return true;
        if (channelPhotoId != existing.PersonalChannelPhotoId) return true;
        if (pinnedStoryIds != existing.PinnedStoryIds) return true;

        return false;
    }

    private static ProfileScanResult EmptyResult(long userId, string? skipReason = null) =>
        new(TelegramUserId: userId,
            Bio: null,
            PersonalChannelId: null,
            PersonalChannelTitle: null,
            PersonalChannelAbout: null,
            HasPinnedStories: false,
            PinnedStoryCaptions: null,
            IsScam: false,
            IsFake: false,
            IsVerified: false,
            Score: 0.0m,
            Outcome: ProfileScanOutcome.Clean,
            AiReason: null,
            AiSignalsDetected: null,
            ContainsNudity: false,
            ExplicitDisplayText: false,
            SkipReason: skipReason);

    private static string OutcomeToTag(ProfileScanOutcome outcome) => outcome switch
    {
        ProfileScanOutcome.Clean => "clean",
        ProfileScanOutcome.HeldForReview => "held_for_review",
        ProfileScanOutcome.Banned => "banned",
        _ => outcome.ToString().ToLowerInvariant()
    };
}
