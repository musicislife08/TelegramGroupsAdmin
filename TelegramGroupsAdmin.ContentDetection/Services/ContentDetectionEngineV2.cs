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
    private readonly ISpamDetectionConfigRepository _configRepository;
    private readonly IFileScanningConfigRepository _fileScanningConfigRepo;
    private readonly IEnumerable<IContentCheckV2> _spamChecksV2;
    private readonly IEnumerable<IContentCheck> _spamChecksV1; // For OpenAI veto only
    private readonly IOpenAITranslationService _translationService;
    private readonly IUrlPreFilterService _preFilterService;
    private readonly SpamDetectionOptions _spamDetectionOptions;

    // SpamAssassin-style thresholds (point-based, not confidence percentage)
    private const double SpamThreshold = 5.0;      // ≥5.0 points = spam
    private const double ReviewThreshold = 3.0;    // 3.0-5.0 points = review queue
    // <3.0 points = allow

    public ContentDetectionEngineV2(
        ILogger<ContentDetectionEngineV2> logger,
        ISpamDetectionConfigRepository configRepository,
        IFileScanningConfigRepository fileScanningConfigRepo,
        IEnumerable<IContentCheckV2> spamChecksV2,
        IEnumerable<IContentCheck> spamChecksV1,
        IOpenAITranslationService translationService,
        IUrlPreFilterService preFilterService,
        IOptions<SpamDetectionOptions> spamDetectionOptions)
    {
        _logger = logger;
        _configRepository = configRepository;
        _fileScanningConfigRepo = fileScanningConfigRepo;
        _spamChecksV2 = spamChecksV2;
        _spamChecksV1 = spamChecksV1;
        _translationService = translationService;
        _preFilterService = preFilterService;
        _spamDetectionOptions = spamDetectionOptions.Value;
    }

    private async Task<SpamDetectionConfig> GetConfigAsync(ContentCheckRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await _configRepository.GetEffectiveConfigAsync(request.ChatId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load spam detection config, using default");
            return new SpamDetectionConfig();
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
                    RecommendedAction = SpamAction.AutoBan,
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
            // OpenAI veto uses V1 check implementation (unchanged)
            var openAICheck = _spamChecksV1.FirstOrDefault(check => check.CheckName == CheckName.OpenAI);
            if (openAICheck != null)
            {
                // Load API key
                var apiKeys = await _fileScanningConfigRepo.GetApiKeysAsync(cancellationToken);
                var openAIApiKey = apiKeys?.OpenAI;

                // Info logging to diagnose API key loading
                _logger.LogInformation("[V2 DEBUG] OpenAI veto check: apiKeys={ApiKeysNull}, openAIApiKey={KeyNull}, keyLength={KeyLength}",
                    apiKeys == null ? "null" : "loaded",
                    openAIApiKey == null ? "null" : "set",
                    openAIApiKey?.Length ?? 0);

                // Skip OpenAI veto if API key not configured
                if (string.IsNullOrWhiteSpace(openAIApiKey))
                {
                    _logger.LogWarning("[V2] OpenAI veto skipped for user {UserId}: API key not configured (apiKeys={ApiKeysNull})",
                        request.UserId, apiKeys == null ? "null" : "loaded but empty");
                    RecordDetectionMetrics(startTimestamp, nonOpenAIResult, activity);
                    return nonOpenAIResult;
                }

                _logger.LogInformation("[V2] Running OpenAI veto check for user {UserId} with API key", request.UserId);

                var vetoRequest = request with { HasSpamFlags = true };

                var checkRequest = BuildOpenAIRequest(vetoRequest, config, openAIConfig, openAIApiKey, cancellationToken);

                // Debug: Log the request details
                if (checkRequest is OpenAICheckRequest openAIReq)
                {
                    _logger.LogInformation("[V2 DEBUG] OpenAI request built: ApiKey={KeyLength} chars, VetoMode={VetoMode}, HasSpamFlags={HasSpamFlags}",
                        openAIReq.ApiKey?.Length ?? 0, openAIReq.VetoMode, openAIReq.HasSpamFlags);
                }

                var vetoResult = await openAICheck.CheckAsync(checkRequest);

                // Convert V1 response to include in final result
                var vetoCheckResult = new ContentCheckResponse
                {
                    CheckName = CheckName.OpenAI,
                    Result = vetoResult.Result,
                    Confidence = vetoResult.Confidence,
                    Details = vetoResult.Details
                };

                var updatedCheckResults = nonOpenAIResult.CheckResults.Append(vetoCheckResult).ToList();

                if (vetoResult.Result == CheckResultType.Clean)
                {
                    _logger.LogInformation("OpenAI vetoed spam detection for user {UserId} with {Confidence}% confidence",
                        request.UserId, vetoResult.Confidence);
                    var vetoedResult = CreateVetoedResult(updatedCheckResults, vetoResult);
                    RecordDetectionMetrics(startTimestamp, vetoedResult, activity);
                    return vetoedResult;
                }
                else if (vetoResult.Result == CheckResultType.Review)
                {
                    _logger.LogInformation("OpenAI flagged message for human review for user {UserId}", request.UserId);
                    var reviewResult = CreateReviewResult(updatedCheckResults, vetoResult);
                    RecordDetectionMetrics(startTimestamp, reviewResult, activity);
                    return reviewResult;
                }
                else if (vetoResult.Result == CheckResultType.Spam)
                {
                    // OpenAI CONFIRMED spam - recalculate with OpenAI's score added
                    _logger.LogInformation("OpenAI confirmed spam for user {UserId} with {Confidence}% confidence",
                        request.UserId, vetoResult.Confidence);

                    // Map OpenAI confidence to V2 score (95% = 5.0 points, 80% = 4.0, etc.)
                    var openAIScore = vetoResult.Confidence / 20.0; // 95% → 4.75 points
                    var newTotalScore = (nonOpenAIResult.NetConfidence / 20.0) + openAIScore; // Add to existing

                    var confirmedResult = nonOpenAIResult with
                    {
                        CheckResults = updatedCheckResults,
                        IsSpam = true, // OpenAI confirmed = definitely spam
                        NetConfidence = (int)(newTotalScore * 20), // Recalculate with OpenAI
                        MaxConfidence = Math.Max(nonOpenAIResult.MaxConfidence, vetoResult.Confidence),
                        AvgConfidence = (nonOpenAIResult.AvgConfidence + vetoResult.Confidence) / 2,
                        SpamFlags = nonOpenAIResult.SpamFlags + 1,
                        PrimaryReason = $"OpenAI confirmed spam: {vetoResult.Details} (total score: {newTotalScore:F1})",
                        RecommendedAction = newTotalScore >= 5.0 ? SpamAction.AutoBan : SpamAction.ReviewQueue,
                        ShouldVeto = false // Veto completed, confirmed spam
                    };

                    RecordDetectionMetrics(startTimestamp, confirmedResult, activity);
                    return confirmedResult;
                }

                // Fallback: OpenAI returned unknown result, continue with original
                nonOpenAIResult = nonOpenAIResult with { CheckResults = updatedCheckResults };
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
        foreach (var check in _spamChecksV2)
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
        var isSpam = totalScore >= ReviewThreshold; // ≥3.0 is spam (may need review)
        var recommendedAction = DetermineActionFromScore(totalScore);

        // Veto mode: Run OpenAI to confirm ANY spam detection (even low confidence)
        // This reduces false positives by having AI double-check all spam signals
        var shouldVeto = totalScore > 0 && config.OpenAI.VetoMode;

        var primaryReason = totalScore >= ReviewThreshold
            ? $"Additive score: {totalScore:F1} points (threshold: {SpamThreshold:F1})"
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

    private bool ShouldRunCheckV2(IContentCheckV2 check, ContentCheckRequest request, SpamDetectionConfig config)
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
        SpamDetectionConfig config,
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
                ApiKey = "", // Will be loaded by check
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
                ApiKey = "", // Will be loaded by check
                CancellationToken = cancellationToken
            },

            _ => throw new InvalidOperationException($"Unknown check type: {check.CheckName}")
        };
    }

    private ContentCheckRequestBase BuildOpenAIRequest(
        ContentCheckRequest originalRequest,
        SpamDetectionConfig config,
        OpenAIConfigDb? openAIConfig,
        string? openAIApiKey,
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
            ApiKey = openAIApiKey ?? "",
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

    private SpamAction DetermineActionFromScore(double totalScore)
    {
        if (totalScore >= SpamThreshold)
            return SpamAction.AutoBan; // ≥5.0 points

        if (totalScore >= ReviewThreshold)
            return SpamAction.ReviewQueue; // 3.0-5.0 points

        return SpamAction.Allow; // <3.0 points
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
            RecommendedAction = SpamAction.Allow,
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
            RecommendedAction = SpamAction.ReviewQueue,
            ShouldVeto = false
        };
    }

    private async Task<ContentCheckRequest> PreprocessMessageAsync(
        ContentCheckRequest request,
        SpamDetectionConfig config,
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
