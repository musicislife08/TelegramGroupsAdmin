using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.ContentDetection.Abstractions;
using TelegramGroupsAdmin.Configuration.Models.ContentDetection;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.Services.AI;
using TelegramGroupsAdmin.Core.Telemetry;
using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// V2 spam detection engine using SpamAssassin-style additive scoring
/// Fixes the critical bug where abstentions (finding nothing) voted "Clean" and cancelled spam signals
/// Key change: Score = Σ(positive_scores) instead of Net = Σ(spam) - Σ(clean)
/// </summary>
public class ContentDetectionEngineV2 : IContentDetectionEngine
{
    private readonly ILogger<ContentDetectionEngineV2> _logger;
    private readonly IContentDetectionConfigRepository _configRepository;
    private readonly ISystemConfigRepository _systemConfigRepo;
    private readonly IPromptVersionRepository _promptVersionRepo;
    private readonly IEnumerable<IContentCheckV2> _contentChecksV2;
    private readonly IAITranslationService _translationService;
    private readonly IUrlPreFilterService _preFilterService;
    private readonly ContentDetectionOptions _spamDetectionOptions;

    // SpamAssassin-style thresholds defined in ContentDetectionConstants
    // ≥5.0 points = spam, 3.0-5.0 = review queue, <3.0 = allow

    public ContentDetectionEngineV2(
        ILogger<ContentDetectionEngineV2> logger,
        IContentDetectionConfigRepository configRepository,
        ISystemConfigRepository systemConfigRepo,
        IPromptVersionRepository promptVersionRepo,
        IEnumerable<IContentCheckV2> contentChecksV2,
        IAITranslationService translationService,
        IUrlPreFilterService preFilterService,
        IOptions<ContentDetectionOptions> spamDetectionOptions)
    {
        _logger = logger;
        _configRepository = configRepository;
        _systemConfigRepo = systemConfigRepo;
        _promptVersionRepo = promptVersionRepo;
        _contentChecksV2 = contentChecksV2;
        _translationService = translationService;
        _preFilterService = preFilterService;
        _spamDetectionOptions = spamDetectionOptions.Value;
    }

    private async Task<ContentDetectionConfig> GetConfigAsync(ContentCheckRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await _configRepository.GetEffectiveConfigAsync(request.ChatId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load spam detection config, using default");
            return new ContentDetectionConfig();
        }
    }

    public async Task<ContentDetectionResult> CheckMessageAsync(ContentCheckRequest request, CancellationToken cancellationToken = default)
    {
        using var activity = TelemetryConstants.SpamDetection.StartActivity("spam_detection.check_message_v2");
        activity?.SetTag("user_id", request.UserId);
        activity?.SetTag("chat_id", request.ChatId);
        activity?.SetTag("engine_version", "v2");

        var startTimestamp = Stopwatch.GetTimestamp();

        // Load latest config from database
        var config = await GetConfigAsync(request, cancellationToken);

        // Phase 4.13: URL Pre-Filter - Check for hard-blocked domains FIRST
        if (!string.IsNullOrWhiteSpace(request.Message))
        {
            var hardBlock = await _preFilterService.CheckHardBlockAsync(request.Message, request.ChatId, cancellationToken);

            if (hardBlock.ShouldBlock)
            {
                _logger.LogWarning(
                    "Hard block triggered for {User} in chat {ChatId}: {Reason}",
                    LogDisplayName.UserDebug(request.UserName, request.UserId), request.ChatId, hardBlock.Reason);

                var hardBlockResult = new ContentDetectionResult
                {
                    IsSpam = true,
                    HardBlock = hardBlock,
                    NetConfidence = 100,
                    MaxConfidence = 100,
                    AvgConfidence = 100,
                    SpamFlags = 1,
                    PrimaryReason = hardBlock.Reason ?? "Hard block policy violation",
                    RecommendedAction = DetectionAction.AutoBan,
                    ShouldVeto = false,
                    CheckResults =
                    [
                        new()
                        {
                            CheckName = CheckName.UrlBlocklist,
                            Result = CheckResultType.HardBlock,
                            Details = hardBlock.Reason ?? "Domain on hard block list",
                            Confidence = 100
                        }
                    ]
                };

                RecordDetectionMetrics(startTimestamp, hardBlockResult, activity);
                return hardBlockResult;
            }
        }

        // Run all non-AI pipeline checks
        var pipelineResult = await RunPipelineChecksAsync(request, cancellationToken);

        // Load AI provider config (connection + feature settings)
        var aiProviderConfig = await _systemConfigRepo.GetAIProviderConfigAsync(cancellationToken);
        var spamFeatureConfig = aiProviderConfig?.Features.GetValueOrDefault(AIFeatureType.SpamDetection);
        var connection = aiProviderConfig?.Connections.FirstOrDefault(c => c.Id == spamFeatureConfig?.ConnectionId);

        // Check BOTH infrastructure.Enabled (connection active) AND spamdetection.AIVeto.Enabled (per-chat)
        var infrastructureEnabled = connection?.Enabled ?? false;
        var spamDetectionEnabled = config.AIVeto.Enabled;

        // AI always runs as veto to confirm ANY spam detection (even low confidence)
        // This reduces false positives by having AI double-check all spam signals
        var hasAnySpam = pipelineResult.NetConfidence > 0;  // Any check contributed points (NetConfidence = totalScore * 20)
        var shouldRunAIVeto = hasAnySpam
            && infrastructureEnabled  // Global kill switch
            && spamDetectionEnabled;  // Per-chat spam detection toggle

        _logger.LogDebug("AI veto decision: HasAnySpam={HasAnySpam}, NetConf={NetConf}, InfraEnabled={InfraEnabled}, SpamDetectionEnabled={SpamEnabled}, ShouldRun={ShouldRun}",
            hasAnySpam, pipelineResult.NetConfidence, infrastructureEnabled, spamDetectionEnabled, shouldRunAIVeto);

        if (shouldRunAIVeto)
        {
            // AI veto using V2 check with proper scoring
            var aiVetoCheck = _contentChecksV2.FirstOrDefault(check => check.CheckName == CheckName.OpenAI);
            if (aiVetoCheck != null)
            {
                // Fetch custom prompt from prompt_versions table (null = use default prompt)
                var activePrompt = await _promptVersionRepo.GetActiveVersionAsync(request.ChatId, cancellationToken);
                var systemPrompt = activePrompt?.PromptText;

                _logger.LogDebug("Running AI veto check for {User} (custom prompt: {HasCustom})",
                    request.UserName ?? $"User {request.UserId}", systemPrompt != null);

                var vetoRequest = request with { HasSpamFlags = true };
                var checkRequest = BuildAIRequest(vetoRequest, config, spamFeatureConfig, systemPrompt, pipelineResult.OcrExtractedText, pipelineResult.VisionAnalysisText, cancellationToken);
                var vetoResultV2 = await aiVetoCheck.CheckAsync(checkRequest);

                // Convert V2 response to ContentCheckResponse
                var vetoCheckResult = new ContentCheckResponse
                {
                    CheckName = CheckName.OpenAI,
                    Result = vetoResultV2.Score == 0.0 || vetoResultV2.Abstained
                        ? CheckResultType.Clean
                        : CheckResultType.Spam,
                    Confidence = (int)(vetoResultV2.Score * 20), // V2 score (0-5) → confidence (0-100)
                    Details = vetoResultV2.Details
                };

                List<ContentCheckResponse> updatedCheckResults = [..pipelineResult.CheckResults, vetoCheckResult];

                // AI abstained (API error, timeout, rate limit) - defer to pipeline verdict
                if (vetoResultV2.Abstained)
                {
                    _logger.LogWarning("AI veto abstained for {User} ({Details}), deferring to pipeline verdict (NetConf={NetConf})",
                        LogDisplayName.UserDebug(request.UserName, request.UserId), vetoResultV2.Details, pipelineResult.NetConfidence);

                    // Return pipeline result with AI check appended for visibility
                    var deferredResult = pipelineResult with { CheckResults = updatedCheckResults };
                    RecordDetectionMetrics(startTimestamp, deferredResult, activity);
                    return deferredResult;
                }

                // AI ran successfully and returned clean (score = 0.0, not abstained) - veto the spam detection
                if (vetoResultV2.Score == 0.0)
                {
                    _logger.LogInformation("AI vetoed spam detection for {User} (clean result with 0.0 score)",
                        request.UserName ?? $"User {request.UserId}");
                    var vetoedResult = CreateVetoedResult(updatedCheckResults, vetoCheckResult);
                    RecordDetectionMetrics(startTimestamp, vetoedResult, activity);
                    return vetoedResult;
                }

                // AI confirmed spam - add score to total
                _logger.LogDebug("AI confirmed spam for {User} with score {Score}",
                    request.UserName ?? $"User {request.UserId}", vetoResultV2.Score);

                var newTotalScore = (pipelineResult.NetConfidence / 20.0) + vetoResultV2.Score;

                var confirmedResult = pipelineResult with
                {
                    CheckResults = updatedCheckResults,
                    IsSpam = true,
                    NetConfidence = (int)(newTotalScore * 20),
                    MaxConfidence = Math.Max(pipelineResult.MaxConfidence, vetoCheckResult.Confidence),
                    AvgConfidence = (pipelineResult.AvgConfidence + vetoCheckResult.Confidence) / 2,
                    SpamFlags = pipelineResult.SpamFlags + 1,
                    PrimaryReason = $"AI confirmed spam: {vetoResultV2.Details} (total score: {newTotalScore:F1})",
                    RecommendedAction = newTotalScore >= 5.0 ? DetectionAction.AutoBan : DetectionAction.ReviewQueue,
                    ShouldVeto = false
                };

                RecordDetectionMetrics(startTimestamp, confirmedResult, activity);
                return confirmedResult;
            }
        }

        RecordDetectionMetrics(startTimestamp, pipelineResult, activity);
        return pipelineResult;
    }

    public async Task<ContentDetectionResult> RunPipelineChecksAsync(ContentCheckRequest request, CancellationToken cancellationToken = default)
    {
        var config = await GetConfigAsync(request, cancellationToken);
        // Note: Translation handled upstream in MessageProcessingService

        var checkResponsesV2 = new List<ContentCheckResponseV2>();
        var totalScore = 0.0;

        // Run all V2 checks (they return scores, not votes)
        foreach (var check in _contentChecksV2)
        {
            if (!ShouldRunCheckV2(check, request, config))
                continue;

            try
            {
                var checkRequest = BuildRequestForCheckV2(check, request, config, cancellationToken);
                var checkResult = await ExecuteCheckWithTelemetryAsync(check, checkRequest);
                checkResponsesV2.Add(checkResult);

                // V2 additive scoring: Sum all scores (abstentions contribute 0)
                if (!checkResult.Abstained)
                {
                    totalScore += checkResult.Score;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error running {CheckName} for {User}", check.CheckName, LogDisplayName.UserDebug(request.UserName, request.UserId));
                // Continue with other checks
            }
        }

        // Convert V2 responses to V1 format for backward compatibility
        var checkResultsV1 = ConvertV2ResponsesToV1(checkResponsesV2, totalScore);

        // Extract OCR text and Vision analysis from ImageSpam check for downstream veto use
        var imageSpamResponse = checkResponsesV2.FirstOrDefault(r => r.CheckName == CheckName.ImageSpam);
        var ocrExtractedText = imageSpamResponse?.OcrExtractedText;
        var visionAnalysisText = imageSpamResponse?.VisionAnalysisText;

        // Determine action based on total score (SpamAssassin-style thresholds)
        var isSpam = totalScore >= ContentDetectionConstants.ReviewThreshold; // ≥3.0 is spam (may need review)
        var recommendedAction = DetermineActionFromScore(totalScore);

        // AI veto runs on any spam detection to reduce false positives
        // (actual veto execution happens in CheckMessageAsync based on config.AIVeto.Enabled)
        var shouldVeto = totalScore > 0;

        var primaryReason = totalScore >= ContentDetectionConstants.ReviewThreshold
            ? $"Additive score: {totalScore:F1} points (threshold: {ContentDetectionConstants.SpamThreshold:F1})"
            : "No spam detected";

        // Map to V1 result format for backward compatibility
        var result = new ContentDetectionResult
        {
            IsSpam = isSpam,
            MaxConfidence = (int)(totalScore * 20), // Scale 5.0 → 100 for display
            AvgConfidence = (int)(totalScore * 20),
            SpamFlags = checkResultsV1.Count(r => r.Result == CheckResultType.Spam),
            NetConfidence = (int)(totalScore * 20),
            CheckResults = checkResultsV1,
            PrimaryReason = primaryReason,
            RecommendedAction = recommendedAction,
            ShouldVeto = shouldVeto,
            OcrExtractedText = ocrExtractedText,
            VisionAnalysisText = visionAnalysisText
        };

        _logger.LogDebug("V2 spam check complete: Score={TotalScore:F1}, IsSpam={IsSpam}, Action={Action}",
            totalScore, result.IsSpam, result.RecommendedAction);

        return result;
    }

    private bool ShouldRunCheckV2(IContentCheckV2 check, ContentCheckRequest request, ContentDetectionConfig config)
    {
        var enabled = check.CheckName switch
        {
            CheckName.StopWords => config.StopWords.Enabled,
            CheckName.Bayes => config.Bayes.Enabled,
            // CAS moved to user join flow (WelcomeService) - checks USER not MESSAGE
            CheckName.Similarity => config.Similarity.Enabled,
            CheckName.Spacing => config.Spacing.Enabled,
            CheckName.InvisibleChars => config.InvisibleChars.Enabled,
            CheckName.ThreatIntel => config.ThreatIntel.Enabled && request.Urls.Any(),
            CheckName.UrlBlocklist => config.UrlBlocklist.Enabled && request.Urls.Any(),
            CheckName.ImageSpam => config.ImageSpam.Enabled && (request.ImageData != null || !string.IsNullOrEmpty(request.PhotoFileId) || !string.IsNullOrEmpty(request.PhotoLocalPath)),
            CheckName.VideoSpam => config.VideoSpam.Enabled && !string.IsNullOrEmpty(request.VideoLocalPath),
            _ => false
        };

        if (!enabled)
            return false;

        return check.ShouldExecute(request);
    }

    private ContentCheckRequestBase BuildRequestForCheckV2(
        IContentCheckV2 check,
        ContentCheckRequest originalRequest,
        ContentDetectionConfig config,
        CancellationToken cancellationToken)
    {
        // Reuse V1 request building logic - request types are shared between V1/V2
        return check.CheckName switch
        {
            CheckName.StopWords => new StopWordsCheckRequest
            {
                Message = originalRequest.Message ?? "",
                UserId = originalRequest.UserId,
                UserName = originalRequest.UserName,
                ChatId = originalRequest.ChatId,
                ConfidenceThreshold = config.StopWords.ConfidenceThreshold,
                CancellationToken = cancellationToken
            },

            CheckName.Bayes => new BayesCheckRequest
            {
                Message = originalRequest.Message ?? "",
                UserId = originalRequest.UserId,
                UserName = originalRequest.UserName,
                ChatId = originalRequest.ChatId,
                MinMessageLength = config.MinMessageLength,
                MinSpamProbability = (int)config.Bayes.MinSpamProbability,
                CancellationToken = cancellationToken
            },

            // CAS moved to user join flow (WelcomeService) - checks USER not MESSAGE

            CheckName.Similarity => new SimilarityCheckRequest
            {
                Message = originalRequest.Message ?? "",
                UserId = originalRequest.UserId,
                UserName = originalRequest.UserName,
                ChatId = originalRequest.ChatId,
                MinMessageLength = config.MinMessageLength,
                SimilarityThreshold = config.Similarity.Threshold,
                CancellationToken = cancellationToken
            },

            CheckName.Spacing => new SpacingCheckRequest
            {
                Message = originalRequest.Message ?? "",
                UserId = originalRequest.UserId,
                UserName = originalRequest.UserName,
                ChatId = originalRequest.ChatId,
                ConfidenceThreshold = 70,
                SuspiciousRatioThreshold = config.Spacing.ShortWordRatioThreshold,
                CancellationToken = cancellationToken
            },

            CheckName.InvisibleChars => new InvisibleCharsCheckRequest
            {
                Message = originalRequest.Message ?? "",
                UserId = originalRequest.UserId,
                UserName = originalRequest.UserName,
                ChatId = originalRequest.ChatId,
                ConfidenceThreshold = 80,
                CancellationToken = cancellationToken
            },

            CheckName.ThreatIntel => new ThreatIntelCheckRequest
            {
                Message = originalRequest.Message ?? "",
                UserId = originalRequest.UserId,
                UserName = originalRequest.UserName,
                ChatId = originalRequest.ChatId,
                Urls = originalRequest.Urls ?? [],
                VirusTotalApiKey = _spamDetectionOptions.ApiKey,
                ConfidenceThreshold = 85,
                CancellationToken = cancellationToken
            },

            CheckName.UrlBlocklist => new UrlBlocklistCheckRequest
            {
                Message = originalRequest.Message ?? "",
                UserId = originalRequest.UserId,
                UserName = originalRequest.UserName,
                ChatId = originalRequest.ChatId,
                Urls = originalRequest.Urls ?? [],
                ConfidenceThreshold = 90,
                CancellationToken = cancellationToken
            },

            CheckName.ImageSpam => new ImageCheckRequest
            {
                Message = originalRequest.Message ?? "",
                UserId = originalRequest.UserId,
                UserName = originalRequest.UserName,
                ChatId = originalRequest.ChatId,
                PhotoFileId = originalRequest.PhotoFileId ?? "",
                PhotoUrl = originalRequest.PhotoUrl,
                PhotoLocalPath = originalRequest.PhotoLocalPath,
                CustomPrompt = null,
                ConfidenceThreshold = 80,
                CancellationToken = cancellationToken
            },

            CheckName.VideoSpam => new VideoCheckRequest
            {
                Message = originalRequest.Message ?? "",
                UserId = originalRequest.UserId,
                UserName = originalRequest.UserName,
                ChatId = originalRequest.ChatId,
                VideoLocalPath = originalRequest.VideoLocalPath ?? "",
                CustomPrompt = null,
                ConfidenceThreshold = 80,
                CancellationToken = cancellationToken
            },

            _ => throw new InvalidOperationException($"Unknown check type: {check.CheckName}")
        };
    }

    private AIVetoCheckRequest BuildAIRequest(
        ContentCheckRequest originalRequest,
        ContentDetectionConfig config,
        AIFeatureConfig? featureConfig,
        string? systemPrompt,
        string? ocrExtractedText,
        string? visionAnalysisText,
        CancellationToken cancellationToken)
    {
        return new AIVetoCheckRequest
        {
            Message = originalRequest.Message ?? "",
            UserId = originalRequest.UserId,
            UserName = originalRequest.UserName,
            ChatId = originalRequest.ChatId,
            SystemPrompt = systemPrompt, // From prompt_versions table (null = use default)
            HasSpamFlags = originalRequest.HasSpamFlags,
            MinMessageLength = config.MinMessageLength,
            CheckShortMessages = config.AIVeto.CheckShortMessages,
            MessageHistoryCount = config.AIVeto.MessageHistoryCount,
            Model = featureConfig?.Model ?? "gpt-4o-mini",
            MaxTokens = featureConfig?.MaxTokens ?? 500,
            OcrExtractedText = ocrExtractedText,
            VisionAnalysisText = visionAnalysisText,
            CancellationToken = cancellationToken
        };
    }

    private async Task<ContentCheckResponseV2> ExecuteCheckWithTelemetryAsync(
        IContentCheckV2 check,
        ContentCheckRequestBase checkRequest)
    {
        using var activity = TelemetryConstants.SpamDetection.StartActivity($"spam_detection.check_v2.{check.CheckName}");
        activity?.SetTag("spam_detection.check_name", check.CheckName);
        activity?.SetTag("spam_detection.engine_version", "v2");

        var startTimestamp = Stopwatch.GetTimestamp();

        var result = await check.CheckAsync(checkRequest);

        var durationMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

        // Record metrics
        TelemetryConstants.SpamDetectionDuration.Record(durationMs,
            new KeyValuePair<string, object?>("algorithm", check.CheckName),
            new KeyValuePair<string, object?>("version", "v2"));

        var resultType = result.Abstained ? "abstained" : (result.Score >= 1.0 ? "spam" : "low_confidence");

        TelemetryConstants.SpamDetections.Add(1,
            new KeyValuePair<string, object?>("algorithm", check.CheckName),
            new KeyValuePair<string, object?>("result", resultType),
            new KeyValuePair<string, object?>("version", "v2"));

        if (activity != null)
        {
            activity.SetTag("spam_detection.score", result.Score);
            activity.SetTag("spam_detection.abstained", result.Abstained);
            activity.SetTag("spam_detection.duration_ms", durationMs);
        }

        return result;
    }

    private List<ContentCheckResponse> ConvertV2ResponsesToV1(List<ContentCheckResponseV2> v2Responses, double totalScore)
    {
        // Convert V2 score-based responses to V1 vote-based responses for backward compatibility
        return v2Responses.Select(v2 =>
        {
            var result = v2.Abstained ? CheckResultType.Clean : CheckResultType.Spam;
            var confidence = v2.Abstained ? 0 : (int)(v2.Score * 20); // Scale 5.0 → 100

            return new ContentCheckResponse
            {
                CheckName = v2.CheckName,
                Result = result,
                Confidence = confidence,
                Details = v2.Details,
                Error = v2.Error
            };
        }).ToList();
    }

    private DetectionAction DetermineActionFromScore(double totalScore)
    {
        if (totalScore >= ContentDetectionConstants.SpamThreshold)
            return DetectionAction.AutoBan; // ≥5.0 points

        if (totalScore >= ContentDetectionConstants.ReviewThreshold)
            return DetectionAction.ReviewQueue; // 3.0-5.0 points

        return DetectionAction.Allow; // <3.0 points
    }

    private ContentDetectionResult CreateVetoedResult(List<ContentCheckResponse> checkResults, ContentCheckResponse vetoResult)
    {
        return new ContentDetectionResult
        {
            IsSpam = false,
            MaxConfidence = vetoResult.Confidence,
            AvgConfidence = vetoResult.Confidence,
            SpamFlags = 0,
            NetConfidence = 0,
            CheckResults = checkResults,
            PrimaryReason = vetoResult.Details,
            RecommendedAction = DetectionAction.Allow,
            ShouldVeto = false
        };
    }

    private static void RecordDetectionMetrics(long startTimestamp, ContentDetectionResult result, Activity? activity)
    {
        var durationMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

        TelemetryConstants.SpamDetectionDuration.Record(durationMs,
            new KeyValuePair<string, object?>("algorithm", "pipeline"),
            new KeyValuePair<string, object?>("version", "v2"));

        var resultType = result.IsSpam ? "spam" : "clean";
        TelemetryConstants.SpamDetections.Add(1,
            new KeyValuePair<string, object?>("algorithm", "pipeline"),
            new KeyValuePair<string, object?>("result", resultType),
            new KeyValuePair<string, object?>("version", "v2"));

        if (activity != null)
        {
            activity.SetTag("spam_detection.is_spam", result.IsSpam);
            activity.SetTag("spam_detection.net_confidence", result.NetConfidence);
            activity.SetTag("spam_detection.spam_flags", result.SpamFlags);
            activity.SetTag("spam_detection.checks_run", result.CheckResults.Count);
            activity.SetTag("spam_detection.duration_ms", durationMs);
        }
    }
}
