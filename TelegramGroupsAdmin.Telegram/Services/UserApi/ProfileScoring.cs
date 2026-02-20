using System.Text.Json;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services.AI;

namespace TelegramGroupsAdmin.Telegram.Services.UserApi;

/// <summary>
/// Result of a profile scan containing all extracted data, computed score, and outcome.
/// </summary>
public record ProfileScanResult(
    long TelegramUserId,
    string? Bio,
    long? PersonalChannelId,
    string? PersonalChannelTitle,
    string? PersonalChannelAbout,
    bool HasPinnedStories,
    string? PinnedStoryCaptions,
    bool IsScam,
    bool IsFake,
    bool IsVerified,
    decimal Score,
    ProfileScanOutcome Outcome,
    string? AiReason,
    string[]? AiSignalsDetected);

/// <summary>
/// Two-layer scoring engine for profile risk assessment.
/// Layer 1: Cheap rule-based pre-filters (instant, run first).
/// Layer 2: AI vision analysis (expensive, skipped if Layer 1 already hits ban threshold).
/// </summary>
internal sealed class ProfileScoringEngine(
    IUrlPreFilterService urlPreFilter,
    IStopWordsRepository stopWordsRepository,
    IChatService chatService,
    ILogger logger)
{
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
    /// <param name="banThreshold">Score at or above which the user should be auto-banned.</param>
    /// <param name="notifyThreshold">Score at or above which admins should be notified.</param>
    /// <param name="ct">Cancellation token.</param>
    internal async Task<(decimal Score, ProfileScanOutcome Outcome, string? AiReason, string[]? AiSignals)> ScoreAsync(
        ProfileData profile,
        IReadOnlyList<ImageInput> images,
        decimal banThreshold,
        decimal notifyThreshold,
        CancellationToken ct)
    {
        // ── Layer 1: Rule-based pre-filters ──
        var ruleScore = await RunRuleBasedScoringAsync(profile, ct);

        if (ruleScore >= banThreshold)
        {
            logger.LogInformation("Profile scan for user {UserId}: rule-based score {Score} >= ban threshold {Threshold}, skipping AI",
                profile.UserId, ruleScore, banThreshold);
            return (Cap(ruleScore), ProfileScanOutcome.Banned, "Rule-based detection triggered ban threshold", null);
        }

        // ── Layer 2: AI vision analysis ──
        var (aiScore, aiReason, aiSignals) = await RunAiScoringAsync(profile, images, ct);
        var totalScore = Cap(ruleScore + aiScore);

        var outcome = totalScore >= banThreshold
            ? ProfileScanOutcome.Banned
            : totalScore >= notifyThreshold
                ? ProfileScanOutcome.HeldForReview
                : ProfileScanOutcome.Clean;

        logger.LogInformation(
            "Profile scan for user {UserId}: rule={RuleScore}, ai={AiScore}, total={TotalScore}, outcome={Outcome}",
            profile.UserId, ruleScore, aiScore, totalScore, outcome);

        return (totalScore, outcome, aiReason, aiSignals);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Layer 1: Rule-based pre-filters
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task<decimal> RunRuleBasedScoringAsync(ProfileData profile, CancellationToken ct)
    {
        var score = 0.0m;

        // Telegram-flagged accounts → instant max score
        if (profile.IsScam || profile.IsFake)
        {
            logger.LogInformation("Profile scan for user {UserId}: Telegram-flagged (is_scam={IsScam}, is_fake={IsFake})",
                profile.UserId, profile.IsScam, profile.IsFake);
            return MaxScore;
        }

        // Aggregate all text content for URL and keyword checks
        var allText = AggregateText(profile);
        if (string.IsNullOrWhiteSpace(allText))
            return score;

        // Check for blocked URLs
        var hardBlock = await urlPreFilter.CheckHardBlockAsync(allText, profile.Chat, ct);
        if (hardBlock.ShouldBlock)
        {
            logger.LogInformation("Profile scan for user {UserId}: blocked URL detected ({Domain})",
                profile.UserId, hardBlock.BlockedDomain);
            score += 3.0m;
        }

        // Check for stop words
        var stopWords = (await stopWordsRepository.GetEnabledStopWordsAsync(ct)).ToList();
        if (stopWords.Count > 0)
        {
            var lowerText = allText.ToLowerInvariant();
            var matchedWords = stopWords.Where(w => lowerText.Contains(w.ToLowerInvariant())).ToList();
            if (matchedWords.Count > 0)
            {
                logger.LogInformation("Profile scan for user {UserId}: stop words detected: {Words}",
                    profile.UserId, string.Join(", ", matchedWords));
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

    private async Task<(decimal Score, string? Reason, string[]? Signals)> RunAiScoringAsync(
        ProfileData profile,
        IReadOnlyList<ImageInput> images,
        CancellationToken ct)
    {
        if (!await chatService.IsFeatureAvailableAsync(AIFeatureType.ProfileScan, ct))
        {
            logger.LogWarning("ProfileScan AI feature not configured — only rule-based scoring active");
            return (0.0m, null, null);
        }

        var systemPrompt = ProfileScanPrompts.BuildSystemPrompt();
        var userPrompt = ProfileScanPrompts.BuildUserPrompt(
            profile.FirstName, profile.LastName, profile.Username,
            profile.Bio,
            profile.PersonalChannelTitle, profile.PersonalChannelAbout,
            profile.StoryCount, profile.StoryCaptions,
            images.Count);

        ChatCompletionResult? result;
        var options = new ChatCompletionOptions { JsonMode = true };

        if (images.Count > 0)
        {
            result = images.Count == 1
                ? await chatService.GetVisionCompletionAsync(
                    AIFeatureType.ProfileScan, systemPrompt, userPrompt,
                    images[0].Data, images[0].MimeType, options, ct)
                : await chatService.GetVisionCompletionAsync(
                    AIFeatureType.ProfileScan, systemPrompt, userPrompt,
                    images, options, ct);
        }
        else
        {
            // No images — text-only analysis
            result = await chatService.GetCompletionAsync(
                AIFeatureType.ProfileScan, systemPrompt, userPrompt, options, ct);
        }

        if (result == null)
        {
            logger.LogWarning("Profile scan AI call returned null for user {UserId}", profile.UserId);
            return (0.0m, null, null);
        }

        return ParseAiResponse(result.Content, profile.UserId);
    }

    private (decimal Score, string? Reason, string[]? Signals) ParseAiResponse(string content, long userId)
    {
        try
        {
            var response = JsonSerializer.Deserialize<ProfileScanAIResponse>(content, JsonOptions);
            if (response == null)
            {
                logger.LogWarning("Profile scan AI response deserialized to null for user {UserId}", userId);
                return (0.0m, null, null);
            }

            if (!response.Spam)
                return (0.0m, response.Reason, response.SignalsDetected);

            // Map confidence to points
            var score = response.Confidence switch
            {
                >= 80 => 4.5m,  // High confidence explicit/spam
                >= 60 => 2.5m,  // Suspicious — flag for review
                _ => 0.0m       // Low confidence — treat as clean
            };

            return (score, response.Reason, response.SignalsDetected);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse profile scan AI response for user {UserId}: {Content}",
                userId, content[..Math.Min(content.Length, 200)]);
            return (0.0m, null, null);
        }
    }

    private static decimal Cap(decimal score) => Math.Min(score, MaxScore);
}

/// <summary>
/// Extracted profile data passed to the scoring engine.
/// Decouples scoring from the WTelegram API types.
/// </summary>
internal record ProfileData(
    long UserId,
    ChatIdentity Chat,
    string? FirstName,
    string? LastName,
    string? Username,
    string? Bio,
    long? PersonalChannelId,
    string? PersonalChannelTitle,
    string? PersonalChannelAbout,
    bool HasPinnedStories,
    string? PinnedStoryCaptions,
    int StoryCount,
    IReadOnlyList<string>? StoryCaptions,
    bool IsScam,
    bool IsFake,
    bool IsVerified);
