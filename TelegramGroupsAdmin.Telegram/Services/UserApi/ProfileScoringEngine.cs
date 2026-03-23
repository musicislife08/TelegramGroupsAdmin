using System.Text.Json;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services.AI;

namespace TelegramGroupsAdmin.Telegram.Services.UserApi;

/// <summary>
/// Two-layer scoring engine for profile risk assessment.
/// Layer 1: Cheap rule-based pre-filters (instant, run first).
/// Layer 2: AI vision analysis (expensive, skipped if Layer 1 already hits ban threshold).
/// </summary>
public sealed class ProfileScoringEngine(
    IUrlPreFilterService urlPreFilter,
    IUrlContentScrapingService urlContentScraping,
    IStopWordsRepository stopWordsRepository,
    IChatService chatService,
    ILogger<ProfileScoringEngine> logger) : IProfileScoringEngine
{
    /// <summary>Result from AI vision analysis (Layer 2).</summary>
    private record AiScoringResult(
        decimal Score,
        string? Reason,
        string[]? Signals,
        bool ContainsNudity = false)
    {
        public static readonly AiScoringResult Empty = new(0.0m, null, null, false);
    }

    private const decimal MaxScore = 5.0m;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Run both scoring layers and return the aggregate score.
    /// </summary>
    /// <param name="profile">Extracted profile data (bio, channel, stories, flags).</param>
    /// <param name="images">Profile photo + story frames + channel photo for vision analysis.</param>
    /// <param name="imageLabels">Labels describing each image (e.g. "Image 1: profile photo, Image 2: pinned story").</param>
    /// <param name="banThreshold">Score at or above which the user should be auto-banned.</param>
    /// <param name="notifyThreshold">Score at or above which admins should be notified.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ScoringResult> ScoreAsync(
        ProfileData profile,
        IReadOnlyList<ImageInput> images,
        string? imageLabels,
        decimal banThreshold,
        decimal notifyThreshold,
        CancellationToken cancellationToken)
    {
        // ── Layer 1: Rule-based pre-filters ──
        var ruleScore = await RunRuleBasedScoringAsync(profile, cancellationToken);

        if (ruleScore >= banThreshold)
        {
            logger.LogInformation("Profile scan for {User}: rule-based score {Score} >= ban threshold {Threshold}, skipping AI",
                profile.User.ToLogInfo(), ruleScore, banThreshold);
            return new ScoringResult(
                Score: Cap(ruleScore),
                Outcome: ProfileScanOutcome.Banned,
                RuleScore: ruleScore,
                AiScore: 0.0m,
                AiReason: "Rule-based detection triggered ban threshold",
                AiSignals: null,
                ContainsNudity: false);
        }

        // ── Layer 2: AI vision analysis ──
        var aiResult = await RunAiScoringAsync(profile, images, imageLabels, cancellationToken);
        var totalScore = Cap(ruleScore + aiResult.Score);

        var outcome = totalScore >= banThreshold
            ? ProfileScanOutcome.Banned
            : totalScore >= notifyThreshold
                ? ProfileScanOutcome.HeldForReview
                : ProfileScanOutcome.Clean;

        logger.LogInformation(
            "Profile scan for {User}: rule={RuleScore}, ai={AiScore}, total={TotalScore}, outcome={Outcome}",
            profile.User.ToLogInfo(), ruleScore, aiResult.Score, totalScore, outcome);

        return new ScoringResult(
            Score: totalScore,
            Outcome: outcome,
            RuleScore: ruleScore,
            AiScore: aiResult.Score,
            AiReason: aiResult.Reason,
            AiSignals: aiResult.Signals,
            ContainsNudity: aiResult.ContainsNudity);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Layer 1: Rule-based pre-filters
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task<decimal> RunRuleBasedScoringAsync(ProfileData profile, CancellationToken cancellationToken)
    {
        var score = 0.0m;

        // Telegram-flagged accounts → instant max score
        if (profile.IsScam || profile.IsFake)
        {
            logger.LogInformation("Profile scan for {User}: Telegram-flagged (is_scam={IsScam}, is_fake={IsFake})",
                profile.User.ToLogInfo(), profile.IsScam, profile.IsFake);
            return MaxScore;
        }

        // Aggregate all text content for URL and keyword checks
        var allText = AggregateText(profile);
        if (string.IsNullOrWhiteSpace(allText))
            return score;

        // Check for blocked URLs
        var hardBlock = await urlPreFilter.CheckHardBlockAsync(allText, profile.Chat, cancellationToken);
        if (hardBlock.ShouldBlock)
        {
            logger.LogInformation("Profile scan for {User}: blocked URL detected ({Domain})",
                profile.User.ToLogInfo(), hardBlock.BlockedDomain);
            score += 3.0m;
        }

        // Check for stop words
        var stopWords = (await stopWordsRepository.GetEnabledStopWordsAsync(cancellationToken)).ToList();
        if (stopWords.Count > 0)
        {
            var lowerText = allText.ToLowerInvariant();
            var matchedWords = stopWords.Where(w => lowerText.Contains(w.ToLowerInvariant())).ToList();
            if (matchedWords.Count > 0)
            {
                logger.LogInformation("Profile scan for {User}: stop words detected: {Words}",
                    profile.User.ToLogInfo(), string.Join(", ", matchedWords));
                score += 1.5m;
            }
        }

        return score;
    }

    private static string AggregateText(ProfileData profile)
    {
        var parts = new List<string>(5);
        if (!string.IsNullOrEmpty(profile.Bio)) parts.Add(profile.Bio);
        if (!string.IsNullOrEmpty(profile.PersonalChannelTitle)) parts.Add(profile.PersonalChannelTitle);
        if (!string.IsNullOrEmpty(profile.PersonalChannelAbout)) parts.Add(profile.PersonalChannelAbout);
        if (!string.IsNullOrEmpty(profile.PinnedStoryCaptions)) parts.Add(profile.PinnedStoryCaptions);
        return string.Join("\n", parts);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Layer 2: AI vision analysis
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task<AiScoringResult> RunAiScoringAsync(
        ProfileData profile,
        IReadOnlyList<ImageInput> images,
        string? imageLabels,
        CancellationToken cancellationToken)
    {
        if (!await chatService.IsFeatureAvailableAsync(AIFeatureType.ProfileScan, cancellationToken))
        {
            logger.LogWarning("ProfileScan AI feature not configured — only rule-based scoring active");
            return AiScoringResult.Empty;
        }

        // Scrape URL metadata for AI context (bio, channel description, story captions may contain URLs)
        string? urlMetadata = null;
        var allText = AggregateText(profile);
        if (!string.IsNullOrWhiteSpace(allText))
        {
            try
            {
                urlMetadata = await urlContentScraping.ScrapeUrlMetadataAsync(allText, cancellationToken);
                if (urlMetadata != null)
                {
                    logger.LogDebug("Profile scan for {User}: scraped URL metadata for AI context",
                        profile.User.ToLogDebug());
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Profile scan for {User}: URL scraping failed, continuing without metadata",
                    profile.User.ToLogDebug());
            }
        }

        var systemPrompt = ProfileScanPrompts.BuildSystemPrompt();
        var userPrompt = ProfileScanPrompts.BuildUserPrompt(
            profile.FirstName, profile.LastName, profile.Username,
            profile.Bio,
            profile.PersonalChannelTitle, profile.PersonalChannelAbout,
            profile.StoryCount, profile.StoryCaptions,
            images.Count, imageLabels, urlMetadata);

        ChatCompletionResult? result;
        var options = new ChatCompletionOptions { JsonMode = true };

        if (images.Count > 0)
        {
            result = images.Count == 1
                ? await chatService.GetVisionCompletionAsync(
                    AIFeatureType.ProfileScan, systemPrompt, userPrompt,
                    images[0].Data, images[0].MimeType, options, cancellationToken)
                : await chatService.GetVisionCompletionAsync(
                    AIFeatureType.ProfileScan, systemPrompt, userPrompt,
                    images, options, cancellationToken);
        }
        else
        {
            // No images — text-only analysis
            result = await chatService.GetCompletionAsync(
                AIFeatureType.ProfileScan, systemPrompt, userPrompt, options, cancellationToken);
        }

        if (result == null)
        {
            logger.LogWarning("Profile scan AI call returned null for {User}", profile.User.ToLogDebug());
            return AiScoringResult.Empty;
        }

        return ParseAiResponse(result.Content, profile.User);
    }

    private AiScoringResult ParseAiResponse(string content, UserIdentity user)
    {
        try
        {
            var response = JsonSerializer.Deserialize<ProfileScanAIResponse>(content, JsonOptions);
            if (response == null)
            {
                logger.LogWarning("Profile scan AI response deserialized to null for {User}", user.ToLogDebug());
                return AiScoringResult.Empty;
            }

            var score = Math.Clamp(response.Score, 0.0m, MaxScore);
            return new AiScoringResult(score, response.Reason, response.SignalsDetected, response.ContainsNudity);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse profile scan AI response for {User}: {Content}",
                user.ToLogDebug(), content[..Math.Min(content.Length, 200)]);
            return AiScoringResult.Empty;
        }
    }

    private static decimal Cap(decimal score) => Math.Min(score, MaxScore);
}
