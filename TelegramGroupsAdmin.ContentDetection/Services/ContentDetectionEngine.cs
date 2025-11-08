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
using TelegramGroupsAdmin.Core.Telemetry;
using OpenAIConfigDb = TelegramGroupsAdmin.Configuration.Models.OpenAIConfig;

namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Core spam detection engine that orchestrates all spam detection checks
/// Loads configuration once, builds strongly-typed requests for each check, and aggregates results
/// OpenAI configuration loaded from database (hot-reload support)
/// </summary>
public class ContentDetectionEngine : IContentDetectionEngine
{
    private readonly ILogger<ContentDetectionEngine> _logger;
    private readonly ISpamDetectionConfigRepository _configRepository;
    private readonly IFileScanningConfigRepository _fileScanningConfigRepo;
    private readonly IEnumerable<IContentCheck> _spamChecks;
    private readonly IOpenAITranslationService _translationService;
    private readonly IUrlPreFilterService _preFilterService;
    private readonly SpamDetectionOptions _spamDetectionOptions;

    public ContentDetectionEngine(
        ILogger<ContentDetectionEngine> logger,
        ISpamDetectionConfigRepository configRepository,
        IFileScanningConfigRepository fileScanningConfigRepo,
        IEnumerable<IContentCheck> spamChecks,
        IOpenAITranslationService translationService,
        IUrlPreFilterService preFilterService,
        IOptions<SpamDetectionOptions> spamDetectionOptions)
    {
        _logger = logger;
        _configRepository = configRepository;
        _fileScanningConfigRepo = fileScanningConfigRepo;
        _spamChecks = spamChecks;
        _translationService = translationService;
        _preFilterService = preFilterService;
        _spamDetectionOptions = spamDetectionOptions.Value;
    }

    private async Task<SpamDetectionConfig> GetConfigAsync(ContentCheckRequest request, CancellationToken cancellationToken)
    {
        try
        {
            // Use per-chat config - ChatId is always provided (non-nullable long)
            return await _configRepository.GetEffectiveConfigAsync(request.ChatId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load spam detection config, using default");
            return new SpamDetectionConfig();
        }
    }

    /// <summary>
    /// Build strongly-typed request for a specific check
    /// Engine decides which checks run and builds the exact request each check needs
    /// </summary>
    private ContentCheckRequestBase BuildRequestForCheck(
        IContentCheck check,
        ContentCheckRequest originalRequest,
        SpamDetectionConfig config,
        OpenAIConfigDb? openAIConfig,
        string? openAIApiKey,
        CancellationToken cancellationToken)
    {
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
                ConfidenceThreshold = 75, // No config property, using default
                CancellationToken = cancellationToken
            },

            CheckName.Spacing => new SpacingCheckRequest
            {
                Message = originalRequest.Message ?? "",
                UserId = originalRequest.UserId,
                UserName = originalRequest.UserName,
                ChatId = originalRequest.ChatId,
                ConfidenceThreshold = 70, // No config property, using default
                SuspiciousRatioThreshold = config.Spacing.ShortWordRatioThreshold,
                CancellationToken = cancellationToken
            },

            CheckName.InvisibleChars => new InvisibleCharsCheckRequest
            {
                Message = originalRequest.Message ?? "",
                UserId = originalRequest.UserId,
                UserName = originalRequest.UserName,
                ChatId = originalRequest.ChatId,
                ConfidenceThreshold = 80, // No config property, using default
                CancellationToken = cancellationToken
            },

            CheckName.OpenAI => new OpenAICheckRequest
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
            },

            CheckName.ThreatIntel => new ThreatIntelCheckRequest
            {
                Message = originalRequest.Message ?? "",
                UserId = originalRequest.UserId,
                UserName = originalRequest.UserName,
                ChatId = originalRequest.ChatId,
                Urls = originalRequest.Urls ?? [],
                VirusTotalApiKey = _spamDetectionOptions.ApiKey,
                ConfidenceThreshold = 85, // No config property, using default
                CancellationToken = cancellationToken
            },

            CheckName.UrlBlocklist => new UrlBlocklistCheckRequest
            {
                Message = originalRequest.Message ?? "",
                UserId = originalRequest.UserId,
                UserName = originalRequest.UserName,
                ChatId = originalRequest.ChatId,
                Urls = originalRequest.Urls ?? [],
                ConfidenceThreshold = 90, // No config property, using default
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
                PhotoLocalPath = originalRequest.PhotoLocalPath, // ML-5: For OCR + hash similarity
                CustomPrompt = null, // No config property
                ConfidenceThreshold = 80, // No config property, using default
                ApiKey = openAIApiKey ?? "",
                CancellationToken = cancellationToken
            },

            CheckName.VideoSpam => new VideoCheckRequest
            {
                Message = originalRequest.Message ?? "",
                UserId = originalRequest.UserId,
                UserName = originalRequest.UserName,
                ChatId = originalRequest.ChatId,
                VideoLocalPath = originalRequest.VideoLocalPath ?? "",
                CustomPrompt = null, // No config property
                ConfidenceThreshold = 80, // No config property, using default
                ApiKey = openAIApiKey ?? "",
                CancellationToken = cancellationToken
            },

            _ => throw new InvalidOperationException($"Unknown check type: {check.CheckName}")
        };
    }

    /// <summary>
    /// Determine if a check should run based on config and request properties
    /// Engine makes all orchestration decisions - checks no longer decide if they run
    /// </summary>
    private bool ShouldRunCheck(IContentCheck check, ContentCheckRequest request, SpamDetectionConfig config)
    {
        // First check if enabled in config
        var enabled = check.CheckName switch
        {
            CheckName.StopWords => config.StopWords.Enabled,
            CheckName.Bayes => config.Bayes.Enabled,
            CheckName.CAS => config.Cas.Enabled,
            CheckName.Similarity => config.Similarity.Enabled,
            CheckName.Spacing => config.Spacing.Enabled,
            CheckName.InvisibleChars => config.InvisibleChars.Enabled,
            CheckName.OpenAI => config.OpenAI.Enabled && (!config.OpenAI.VetoMode || request.HasSpamFlags),
            CheckName.ThreatIntel => config.ThreatIntel.Enabled && request.Urls.Any(),
            CheckName.UrlBlocklist => config.UrlBlocklist.Enabled && request.Urls.Any(),
            CheckName.ImageSpam => config.ImageSpam.Enabled && (request.ImageData != null || !string.IsNullOrEmpty(request.PhotoFileId) || !string.IsNullOrEmpty(request.PhotoLocalPath)),
            CheckName.VideoSpam => config.VideoSpam.Enabled && !string.IsNullOrEmpty(request.VideoLocalPath),
            CheckName.FileScanning => true, // Always run file scanning if check exists
            _ => false
        };

        if (!enabled)
            return false;

        // Then check if check's ShouldExecute allows it
        return check.ShouldExecute(request);
    }

    /// <summary>
    /// Run all applicable spam checks on a message and return aggregated results
    /// Phase 4.13: Includes URL pre-filter for hard blocks (runs before all checks)
    /// </summary>
    public async Task<ContentDetectionResult> CheckMessageAsync(ContentCheckRequest request, CancellationToken cancellationToken = default)
    {
        using var activity = TelemetryConstants.SpamDetection.StartActivity("spam_detection.check_message");
        activity?.SetTag("user_id", request.UserId);
        activity?.SetTag("chat_id", request.ChatId);

        var startTimestamp = Stopwatch.GetTimestamp();

        // Load latest config from database (per-chat or global)
        var config = await GetConfigAsync(request, cancellationToken);

        // Load OpenAI config from database (hot-reload support)
        var openAIConfig = await _fileScanningConfigRepo.GetOpenAIConfigAsync(cancellationToken);
        var apiKeys = await _fileScanningConfigRepo.GetApiKeysAsync(cancellationToken);
        var openAIApiKey = apiKeys?.OpenAI;

        var checkResults = new List<ContentCheckResponse>();

        // Phase 4.13: URL Pre-Filter - Check for hard-blocked domains FIRST (instant policy violation)
        if (!string.IsNullOrWhiteSpace(request.Message))
        {
            var hardBlock = await _preFilterService.CheckHardBlockAsync(request.Message, request.ChatId, cancellationToken);

            if (hardBlock.ShouldBlock)
            {
                _logger.LogWarning(
                    "Hard block triggered for user {UserId} in chat {ChatId}: {Reason}",
                    request.UserId, request.ChatId, hardBlock.Reason);

                // Return immediate hard block result (skip all other checks)
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

        // First, run all non-OpenAI checks
        var nonOpenAIResult = await CheckMessageWithoutOpenAIAsync(request, cancellationToken);
        checkResults.AddRange(nonOpenAIResult.CheckResults);

        // Determine if we should run OpenAI veto check
        var shouldRunOpenAI = nonOpenAIResult.ShouldVeto && config.OpenAI.Enabled && config.OpenAI.VetoMode;

        if (shouldRunOpenAI)
        {
            var openAICheck = _spamChecks.FirstOrDefault(check => check.CheckName == CheckName.OpenAI);
            if (openAICheck != null)
            {
                _logger.LogDebug("Running OpenAI veto check for user {UserId}", request.UserId);

                // Update request to indicate other checks found spam
                var vetoRequest = request with { HasSpamFlags = true };

                if (ShouldRunCheck(openAICheck, vetoRequest, config))
                {
                    var checkRequest = BuildRequestForCheck(openAICheck, vetoRequest, config, openAIConfig, openAIApiKey, cancellationToken);
                    var vetoResult = await openAICheck.CheckAsync(checkRequest);
                    checkResults.Add(vetoResult);

                    // If OpenAI vetoes the spam detection (says it's not spam), override the result
                    // If OpenAI says "Review", pass through for human review
                    if (vetoResult.Result == CheckResultType.Clean)
                    {
                        _logger.LogInformation("OpenAI vetoed spam detection for user {UserId} with {Confidence}% confidence",
                            request.UserId, vetoResult.Confidence);
                        var vetoedResult = CreateVetoedResult(checkResults, vetoResult);
                        RecordDetectionMetrics(startTimestamp, vetoedResult, activity);
                        return vetoedResult;
                    }
                    else if (vetoResult.Result == CheckResultType.Review)
                    {
                        _logger.LogInformation("OpenAI flagged message for human review for user {UserId}",
                            request.UserId);
                        var reviewResult = CreateReviewResult(checkResults, vetoResult);
                        RecordDetectionMetrics(startTimestamp, reviewResult, activity);
                        return reviewResult;
                    }
                }
            }
        }

        var finalResult = AggregateResults(checkResults, config);
        RecordDetectionMetrics(startTimestamp, finalResult, activity);
        return finalResult;
    }

    /// <summary>
    /// Execute a spam check with telemetry instrumentation
    /// </summary>
    private static async Task<ContentCheckResponse> ExecuteCheckWithTelemetryAsync(
        IContentCheck check,
        ContentCheckRequestBase checkRequest)
    {
        using var activity = TelemetryConstants.SpamDetection.StartActivity($"spam_detection.check.{check.CheckName}");
        activity?.SetTag("spam_detection.check_name", check.CheckName);

        var startTimestamp = Stopwatch.GetTimestamp();

        var result = await check.CheckAsync(checkRequest);

        var durationMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

        // Record duration histogram for individual check
        TelemetryConstants.SpamDetectionDuration.Record(durationMs,
            new KeyValuePair<string, object?>("algorithm", check.CheckName));

        // Record detection result counter for individual check
        var resultType = result.Result switch
        {
            CheckResultType.Spam => "spam",
            CheckResultType.Clean => "clean",
            CheckResultType.Review => "review",
            CheckResultType.HardBlock => "hard_block",
            _ => "unknown"
        };

        TelemetryConstants.SpamDetections.Add(1,
            new KeyValuePair<string, object?>("algorithm", check.CheckName),
            new KeyValuePair<string, object?>("result", resultType));

        // Enrich activity with check result details
        if (activity != null)
        {
            activity.SetTag("spam_detection.result", resultType);
            activity.SetTag("spam_detection.confidence", result.Confidence);
            activity.SetTag("spam_detection.duration_ms", durationMs);
        }

        return result;
    }

    /// <summary>
    /// Record telemetry metrics for spam detection execution
    /// </summary>
    private static void RecordDetectionMetrics(long startTimestamp, ContentDetectionResult result, Activity? activity)
    {
        var durationMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

        // Record duration histogram
        TelemetryConstants.SpamDetectionDuration.Record(durationMs,
            new KeyValuePair<string, object?>("algorithm", "pipeline"));

        // Record detection result counter
        var resultType = result.IsSpam ? "spam" : "clean";
        TelemetryConstants.SpamDetections.Add(1,
            new KeyValuePair<string, object?>("algorithm", "pipeline"),
            new KeyValuePair<string, object?>("result", resultType));

        // Enrich activity with result details
        if (activity != null)
        {
            activity.SetTag("spam_detection.is_spam", result.IsSpam);
            activity.SetTag("spam_detection.net_confidence", result.NetConfidence);
            activity.SetTag("spam_detection.spam_flags", result.SpamFlags);
            activity.SetTag("spam_detection.checks_run", result.CheckResults.Count);
            activity.SetTag("spam_detection.duration_ms", durationMs);
        }
    }

    /// <summary>
    /// Run only non-OpenAI checks to determine if message should be vetoed by OpenAI
    /// </summary>
    public async Task<ContentDetectionResult> CheckMessageWithoutOpenAIAsync(ContentCheckRequest request, CancellationToken cancellationToken = default)
    {
        // Load latest config from database (per-chat or global)
        var config = await GetConfigAsync(request, cancellationToken);

        // Load OpenAI config for checks that need it (ImageSpam, VideoSpam)
        var openAIConfig = await _fileScanningConfigRepo.GetOpenAIConfigAsync(cancellationToken);
        var apiKeys = await _fileScanningConfigRepo.GetApiKeysAsync(cancellationToken);
        var openAIApiKey = apiKeys?.OpenAI;

        var checkResults = new List<ContentCheckResponse>();

        // Run all checks in two phases:
        // Phase 1: Run InvisibleChars check on ORIGINAL message (before translation)
        // Phase 2: Translate, then run all other checks on translated message

        var invisibleCharsCheck = _spamChecks.FirstOrDefault(check => check.CheckName == CheckName.InvisibleChars);
        if (invisibleCharsCheck != null && ShouldRunCheck(invisibleCharsCheck, request, config))
        {
            try
            {
                var checkRequest = BuildRequestForCheck(invisibleCharsCheck, request, config, openAIConfig, openAIApiKey, cancellationToken);
                var result = await ExecuteCheckWithTelemetryAsync(invisibleCharsCheck, checkRequest);
                checkResults.Add(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error running InvisibleChars for user {UserId}", request.UserId);
            }
        }

        // Preprocess: Translate foreign language if needed
        var processedRequest = await PreprocessMessageAsync(request, config, cancellationToken);

        // Run all other checks on potentially translated message
        var checks = _spamChecks.Where(check => check.CheckName != CheckName.OpenAI && check.CheckName != CheckName.InvisibleChars).ToList();

        foreach (var check in checks)
        {
            if (!ShouldRunCheck(check, processedRequest, config))
                continue;

            try
            {
                var checkRequest = BuildRequestForCheck(check, processedRequest, config, openAIConfig, openAIApiKey, cancellationToken);
                var result = await ExecuteCheckWithTelemetryAsync(check, checkRequest);
                checkResults.Add(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error running {CheckName} for user {UserId}", check.CheckName, request.UserId);
                // Continue with other checks
            }
        }

        return AggregateResults(checkResults, config);
    }

    /// <summary>
    /// Phase 2.6: Calculate net confidence using weighted voting
    /// Net = Sum(spam check confidences) - Sum(ham check confidences)
    /// Review results are not included in net confidence calculation
    /// </summary>
    private int CalculateNetConfidence(List<ContentCheckResponse> checkResults)
    {
        var spamVotes = 0;
        var hamVotes = 0;

        foreach (var check in checkResults)
        {
            if (check.Result == CheckResultType.Spam)
            {
                spamVotes += check.Confidence;
            }
            else if (check.Result == CheckResultType.Clean)
            {
                hamVotes += check.Confidence;
            }
            // Review results don't contribute to net confidence
        }

        var netConfidence = spamVotes - hamVotes;
        return netConfidence;
    }

    /// <summary>
    /// Aggregate results from multiple spam checks
    /// Phase 2.6: Uses weighted voting (net confidence) for two-tier decision system
    /// Phase 4.5: Handles Review result type from AI-based checks
    /// </summary>
    private ContentDetectionResult AggregateResults(List<ContentCheckResponse> checkResults, SpamDetectionConfig config)
    {
        var spamResults = checkResults.Where(r => r.Result == CheckResultType.Spam).ToList();
        var isSpam = spamResults.Any();
        var spamFlags = spamResults.Count;

        var maxConfidence = isSpam ? spamResults.Max(r => r.Confidence) : 0;
        var avgConfidence = isSpam ? (int)spamResults.Average(r => r.Confidence) : 0;

        // Phase 2.6: Calculate net confidence using weighted voting
        var netConfidence = CalculateNetConfidence(checkResults);

        // Phase 2.6: Two-tier decision system based on net confidence OR high individual confidence
        // Veto if: (Net > ReviewQueueThreshold) OR (any check > MaxConfidenceVetoThreshold) - safety before ban
        // Review queue: Net ≤ ReviewQueueThreshold but > 0 (low confidence spam)
        // Allow: Net ≤ 0 (no spam detected)
        var shouldVeto = (netConfidence > config.ReviewQueueThreshold || maxConfidence > config.MaxConfidenceVetoThreshold)
            && config.OpenAI.VetoMode;

        // Determine recommended action based on net confidence
        var recommendedAction = DetermineActionFromNetConfidence(netConfidence, config);

        // Primary reason is from the highest confidence check
        var primaryReason = isSpam
            ? spamResults.OrderByDescending(r => r.Confidence).First().Details
            : "No spam detected";

        var result = new ContentDetectionResult
        {
            IsSpam = isSpam,
            MaxConfidence = maxConfidence,
            AvgConfidence = avgConfidence,
            SpamFlags = spamFlags,
            CheckResults = checkResults,
            PrimaryReason = primaryReason,
            RecommendedAction = recommendedAction,
            ShouldVeto = shouldVeto,
            NetConfidence = netConfidence // Phase 2.6: Store for analytics
        };

        _logger.LogInformation("Spam check complete: IsSpam={IsSpam}, Net={NetConfidence}, Max={MaxConfidence}, Flags={SpamFlags}, Action={Action}",
            result.IsSpam, result.NetConfidence, result.MaxConfidence, result.SpamFlags, result.RecommendedAction);

        return result;
    }

    /// <summary>
    /// Create result when OpenAI vetoes spam detection
    /// </summary>
    private ContentDetectionResult CreateVetoedResult(List<ContentCheckResponse> checkResults, ContentCheckResponse vetoResult)
    {
        // Calculate net confidence even for vetoed results (for analytics)
        var netConfidence = CalculateNetConfidence(checkResults);

        return new ContentDetectionResult
        {
            IsSpam = false, // Vetoed by OpenAI
            MaxConfidence = vetoResult.Confidence, // OpenAI's confidence that it's NOT spam
            AvgConfidence = vetoResult.Confidence,
            SpamFlags = 0, // Veto overrides all flags
            NetConfidence = netConfidence, // Phase 2.6: Store for analytics
            CheckResults = checkResults,
            PrimaryReason = vetoResult.Details,
            RecommendedAction = SpamAction.Allow,
            ShouldVeto = false // Veto already executed
        };
    }

    /// <summary>
    /// Phase 4.5: Create result when OpenAI flags message for human review
    /// </summary>
    private ContentDetectionResult CreateReviewResult(List<ContentCheckResponse> checkResults, ContentCheckResponse reviewResult)
    {
        // Calculate net confidence for analytics
        var netConfidence = CalculateNetConfidence(checkResults);

        return new ContentDetectionResult
        {
            IsSpam = false, // Don't auto-ban on review
            MaxConfidence = reviewResult.Confidence,
            AvgConfidence = reviewResult.Confidence,
            SpamFlags = 0, // Review means uncertain, not spam
            NetConfidence = netConfidence,
            CheckResults = checkResults,
            PrimaryReason = reviewResult.Details,
            RecommendedAction = SpamAction.ReviewQueue, // Always send to review queue
            ShouldVeto = false
        };
    }

    /// <summary>
    /// Phase 2.6: Determine recommended action based on net confidence (weighted voting)
    /// Net > ReviewQueueThreshold: Run OpenAI veto (safety before ban)
    /// Net ≤ ReviewQueueThreshold AND > 0: Admin review queue (skip OpenAI cost)
    /// Net ≤ 0: Allow (no spam detected)
    /// </summary>
    private SpamAction DetermineActionFromNetConfidence(int netConfidence, SpamDetectionConfig config)
    {
        if (netConfidence > config.ReviewQueueThreshold)
        {
            // High confidence spam - pending OpenAI veto check
            // Will become AutoBan if OpenAI confirms (or if OpenAI disabled)
            return SpamAction.ReviewQueue; // Will be upgraded to AutoBan after OpenAI veto
        }

        if (netConfidence > 0)
        {
            // Low confidence spam - send to admin review
            return SpamAction.ReviewQueue;
        }

        // No spam detected (net confidence ≤ 0)
        return SpamAction.Allow;
    }

    /// <summary>
    /// Preprocess message: translate foreign languages to English if needed
    /// </summary>
    /// <returns>Request with potentially translated text</returns>
    private async Task<ContentCheckRequest> PreprocessMessageAsync(
        ContentCheckRequest request,
        SpamDetectionConfig config,
        CancellationToken cancellationToken)
    {
        // NOTE: Translation now happens in MessageProcessingService before spam detection
        // This method previously translated messages, but was refactored to eliminate double translation
        // The request.Message field already contains translated text if translation was needed

        // Future optimization: Could add Latin script quick-check here to skip checks on non-Latin text
        // that somehow bypassed upstream translation (edge cases)

        await Task.CompletedTask; // Preserve async signature for future preprocessing logic
        return request;
    }


    /// <summary>
    /// Check if message is primarily Latin/ASCII script (likely English) to avoid unnecessary OpenAI translation
    /// </summary>
    /// <param name="message">Message to analyze</param>
    /// <param name="threshold">Latin script ratio threshold (0.0-1.0, default from config)</param>
    private static bool IsLikelyLatinScript(string message, double threshold)
    {
        if (string.IsNullOrWhiteSpace(message))
            return true;

        // Count letter characters and check which script they belong to
        var letterCount = 0;
        var latinCount = 0;

        foreach (var c in message)
        {
            if (char.IsLetter(c))
            {
                letterCount++;
                // Latin, Latin Extended-A, Latin Extended-B ranges (covers Western European languages)
                if ((c >= 0x0041 && c <= 0x007A) ||  // Basic Latin (A-Z, a-z)
                    (c >= 0x00C0 && c <= 0x00FF) ||  // Latin-1 Supplement (À-ÿ)
                    (c >= 0x0100 && c <= 0x017F) ||  // Latin Extended-A (Ā-ſ)
                    (c >= 0x0180 && c <= 0x024F))    // Latin Extended-B (ƀ-ɏ)
                {
                    latinCount++;
                }
            }
        }

        // If no letters, assume it's symbols/numbers (safe to skip translation)
        if (letterCount == 0)
            return true;

        // If >= threshold of letters are Latin script, likely English/Western European language
        return (double)latinCount / letterCount > threshold;
    }
}