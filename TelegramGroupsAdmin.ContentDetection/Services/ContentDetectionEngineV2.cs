using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.ContentDetection.Abstractions;
using TelegramGroupsAdmin.ContentDetection.Configuration;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.Telemetry;
using OpenAIConfigDb = TelegramGroupsAdmin.Configuration.Models.OpenAIConfig;

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
    private readonly ISystemConfigRepository _fileScanningConfigRepo;
    private readonly IEnumerable<IContentCheckV2> _contentChecksV2;
    private readonly IOpenAITranslationService _translationService;
    private readonly IUrlPreFilterService _preFilterService;
    private readonly ContentDetectionOptions _spamDetectionOptions;

    // SpamAssassin-style thresholds defined in ContentDetectionConstants
    // ≥5.0 points = spam, 3.0-5.0 = review queue, <3.0 = allow

    public ContentDetectionEngineV2(
        ILogger<ContentDetectionEngineV2> logger,
        IContentDetectionConfigRepository configRepository,
        ISystemConfigRepository fileScanningConfigRepo,
        IEnumerable<IContentCheckV2> contentChecksV2,
        IOpenAITranslationService translationService,
        IUrlPreFilterService preFilterService,
        IOptions<ContentDetectionOptions> spamDetectionOptions)
    {
        _logger = logger;
        _configRepository = configRepository;
        _fileScanningConfigRepo = fileScanningConfigRepo;
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
                    "Hard block triggered for user {UserId} in chat {ChatId}: {Reason}",
                    request.UserId, request.ChatId, hardBlock.Reason);

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

        // Run all non-OpenAI V2 checks
        var nonOpenAIResult = await CheckMessageWithoutOpenAIAsync(request, cancellationToken);

        // Load infrastructure-level OpenAI config (global kill switch)
        var openAIConfig = await _fileScanningConfigRepo.GetOpenAIConfigAsync(cancellationToken);

        // Check BOTH infrastructure.Enabled (global) AND spamdetection.OpenAI.Enabled (per-chat)
        var infrastructureEnabled = openAIConfig?.Enabled ?? false;
        var spamDetectionEnabled = config.OpenAI.Enabled;

        // Veto mode: Run OpenAI to confirm ANY spam detection (even low confidence)
        // This reduces false positives by having AI double-check all spam signals
        var hasAnySpam = nonOpenAIResult.NetConfidence > 0;  // Any check contributed points (NetConfidence = totalScore * 20)
        var shouldRunOpenAI = hasAnySpam
            && infrastructureEnabled  // Global kill switch
            && spamDetectionEnabled   // Per-chat spam detection toggle
            && config.OpenAI.VetoMode;

        _logger.LogInformation("[V2 DEBUG] OpenAI veto decision: HasAnySpam={HasAnySpam}, NetConf={NetConf}, InfraEnabled={InfraEnabled}, SpamDetectionEnabled={SpamEnabled}, VetoMode={VetoMode}, shouldRunOpenAI={ShouldRun}",
            hasAnySpam, nonOpenAIResult.NetConfidence, infrastructureEnabled, spamDetectionEnabled, config.OpenAI.VetoMode, shouldRunOpenAI);

        if (shouldRunOpenAI)
        {
            // OpenAI veto using V2 check with proper scoring
            var openAICheckV2 = _contentChecksV2.FirstOrDefault(check => check.CheckName == CheckName.OpenAI);
            if (openAICheckV2 != null)
            {
                // Note: API key is injected via ApiKeyDelegatingHandler on the named "OpenAI" HttpClient
                _logger.LogInformation("[V2] Running OpenAI veto check for user {UserId}", request.UserId);

                var vetoRequest = request with { HasSpamFlags = true };
                var checkRequest = BuildOpenAIRequest(vetoRequest, config, openAIConfig, cancellationToken);
                var vetoResultV2 = await openAICheckV2.CheckAsync(checkRequest);

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

                List<ContentCheckResponse> updatedCheckResults = [..nonOpenAIResult.CheckResults, vetoCheckResult];

                // OpenAI abstained (API error, timeout, rate limit) - defer to pipeline verdict
                if (vetoResultV2.Abstained)
                {
                    _logger.LogWarning("OpenAI veto abstained for user {UserId} ({Details}), deferring to pipeline verdict (NetConf={NetConf})",
                        request.UserId, vetoResultV2.Details, nonOpenAIResult.NetConfidence);

                    // Return nonOpenAIResult with OpenAI check appended for visibility
                    var deferredResult = nonOpenAIResult with { CheckResults = updatedCheckResults };
                    RecordDetectionMetrics(startTimestamp, deferredResult, activity);
                    return deferredResult;
                }

                // OpenAI ran successfully and returned clean (score = 0.0, not abstained) - veto the spam detection
                if (vetoResultV2.Score == 0.0)
                {
                    _logger.LogInformation("OpenAI vetoed spam detection for user {UserId} (clean result with 0.0 score)",
                        request.UserId);
                    var vetoedResult = CreateVetoedResult(updatedCheckResults, vetoCheckResult);
                    RecordDetectionMetrics(startTimestamp, vetoedResult, activity);
                    return vetoedResult;
                }

                // OpenAI confirmed spam - add score to total
                _logger.LogInformation("OpenAI confirmed spam for user {UserId} with score {Score}",
                    request.UserId, vetoResultV2.Score);

                var newTotalScore = (nonOpenAIResult.NetConfidence / 20.0) + vetoResultV2.Score;

                var confirmedResult = nonOpenAIResult with
                {
                    CheckResults = updatedCheckResults,
                    IsSpam = true,
                    NetConfidence = (int)(newTotalScore * 20),
                    MaxConfidence = Math.Max(nonOpenAIResult.MaxConfidence, vetoCheckResult.Confidence),
                    AvgConfidence = (nonOpenAIResult.AvgConfidence + vetoCheckResult.Confidence) / 2,
                    SpamFlags = nonOpenAIResult.SpamFlags + 1,
                    PrimaryReason = $"OpenAI confirmed spam: {vetoResultV2.Details} (total score: {newTotalScore:F1})",
                    RecommendedAction = newTotalScore >= 5.0 ? DetectionAction.AutoBan : DetectionAction.ReviewQueue,
                    ShouldVeto = false
                };

                RecordDetectionMetrics(startTimestamp, confirmedResult, activity);
                return confirmedResult;
            }
        }

        RecordDetectionMetrics(startTimestamp, nonOpenAIResult, activity);
        return nonOpenAIResult;
    }

    public async Task<ContentDetectionResult> CheckMessageWithoutOpenAIAsync(ContentCheckRequest request, CancellationToken cancellationToken = default)
    {
        var config = await GetConfigAsync(request, cancellationToken);

        // Preprocess: Translate foreign language if needed
        var processedRequest = await PreprocessMessageAsync(request, config, cancellationToken);

        var checkResponsesV2 = new List<ContentCheckResponseV2>();
        var totalScore = 0.0;

        // Run all V2 checks (they return scores, not votes)
        foreach (var check in _contentChecksV2)
        {
            if (!ShouldRunCheckV2(check, processedRequest, config))
                continue;

            try
            {
                var checkRequest = BuildRequestForCheckV2(check, processedRequest, config, cancellationToken);
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
                _logger.LogError(ex, "Error running {CheckName} for user {UserId}", check.CheckName, request.UserId);
                // Continue with other checks
            }
        }

        // Convert V2 responses to V1 format for backward compatibility
        var checkResultsV1 = ConvertV2ResponsesToV1(checkResponsesV2, totalScore);

        // Determine action based on total score (SpamAssassin-style thresholds)
        var isSpam = totalScore >= ContentDetectionConstants.ReviewThreshold; // ≥3.0 is spam (may need review)
        var recommendedAction = DetermineActionFromScore(totalScore);

        // Veto mode: Run OpenAI to confirm ANY spam detection (even low confidence)
        // This reduces false positives by having AI double-check all spam signals
        var shouldVeto = totalScore > 0 && config.OpenAI.VetoMode;

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
            ShouldVeto = shouldVeto
        };

        _logger.LogInformation("V2 spam check complete: Score={TotalScore:F1}, IsSpam={IsSpam}, Action={Action}",
            totalScore, result.IsSpam, result.RecommendedAction);

        return result;
    }

    private bool ShouldRunCheckV2(IContentCheckV2 check, ContentCheckRequest request, ContentDetectionConfig config)
    {
        var enabled = check.CheckName switch
        {
            CheckName.StopWords => config.StopWords.Enabled,
            CheckName.Bayes => config.Bayes.Enabled,
            CheckName.CAS => config.Cas.Enabled,
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

            CheckName.CAS => new CasCheckRequest
            {
                Message = originalRequest.Message ?? "",
                UserId = originalRequest.UserId,
                UserName = originalRequest.UserName,
                ChatId = originalRequest.ChatId,
                ApiUrl = config.Cas.ApiUrl,
                Timeout = config.Cas.Timeout,
                UserAgent = config.Cas.UserAgent,
                CancellationToken = cancellationToken
            },

            CheckName.Similarity => new SimilarityCheckRequest
            {
                Message = originalRequest.Message ?? "",
                UserId = originalRequest.UserId,
                UserName = originalRequest.UserName,
                ChatId = originalRequest.ChatId,
                MinMessageLength = config.MinMessageLength,
                SimilarityThreshold = config.Similarity.Threshold,
                ConfidenceThreshold = 75,
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

    private ContentCheckRequestBase BuildOpenAIRequest(
        ContentCheckRequest originalRequest,
        ContentDetectionConfig config,
        OpenAIConfigDb? openAIConfig,
        CancellationToken cancellationToken)
    {
        return new OpenAICheckRequest
        {
            Message = originalRequest.Message ?? "",
            UserId = originalRequest.UserId,
            UserName = originalRequest.UserName,
            ChatId = originalRequest.ChatId,
            VetoMode = config.OpenAI.VetoMode,
            SystemPrompt = config.OpenAI.SystemPrompt,
            HasSpamFlags = originalRequest.HasSpamFlags,
            MinMessageLength = config.MinMessageLength,
            CheckShortMessages = config.OpenAI.CheckShortMessages,
            MessageHistoryCount = config.OpenAI.MessageHistoryCount,
            Model = openAIConfig?.Model ?? "gpt-4o-mini",
            MaxTokens = openAIConfig?.MaxTokens ?? 500,
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

    private ContentDetectionResult CreateReviewResult(List<ContentCheckResponse> checkResults, ContentCheckResponse reviewResult)
    {
        return new ContentDetectionResult
        {
            IsSpam = false,
            MaxConfidence = reviewResult.Confidence,
            AvgConfidence = reviewResult.Confidence,
            SpamFlags = 0,
            NetConfidence = 0,
            CheckResults = checkResults,
            PrimaryReason = reviewResult.Details,
            RecommendedAction = DetectionAction.ReviewQueue,
            ShouldVeto = false
        };
    }

    private async Task<ContentCheckRequest> PreprocessMessageAsync(
        ContentCheckRequest request,
        ContentDetectionConfig config,
        CancellationToken cancellationToken)
    {
        // Translation handled upstream in MessageProcessingService
        await Task.CompletedTask;
        return request;
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
