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
using TelegramGroupsAdmin.ContentDetection.Metrics;
using TelegramGroupsAdmin.Core.Telemetry;
using TelegramGroupsAdmin.Core.Extensions;

namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// V2 spam detection engine using SpamAssassin-style additive scoring
/// Fixes the critical bug where abstentions (finding nothing) voted "Clean" and cancelled spam signals
/// Key change: Score = Σ(positive_scores) instead of Net = Σ(spam) - Σ(clean)
/// </summary>
public partial class ContentDetectionEngineV2 : IContentDetectionEngine
{
    private readonly ILogger<ContentDetectionEngineV2> _logger;
    private readonly IContentDetectionConfigRepository _configRepository;
    private readonly ISystemConfigRepository _systemConfigRepo;
    private readonly IPromptVersionRepository _promptVersionRepo;
    private readonly IEnumerable<IContentCheckV2> _contentChecksV2;
    private readonly IUrlPreFilterService _preFilterService;
    private readonly ContentDetectionOptions _spamDetectionOptions;
    private readonly DetectionMetrics _detectionMetrics;

    // Action thresholds are per-chat config (ContentDetectionConfig.AutoBanThreshold / ReviewQueueThreshold)
    // Safety clamps are in ContentDetectionConstants (MinScore / MaxScore)

    public ContentDetectionEngineV2(
        ILogger<ContentDetectionEngineV2> logger,
        IContentDetectionConfigRepository configRepository,
        ISystemConfigRepository systemConfigRepo,
        IPromptVersionRepository promptVersionRepo,
        IEnumerable<IContentCheckV2> contentChecksV2,
        IUrlPreFilterService preFilterService,
        IOptions<ContentDetectionOptions> spamDetectionOptions,
        DetectionMetrics detectionMetrics)
    {
        _logger = logger;
        _configRepository = configRepository;
        _systemConfigRepo = systemConfigRepo;
        _promptVersionRepo = promptVersionRepo;
        _contentChecksV2 = contentChecksV2;
        _preFilterService = preFilterService;
        _spamDetectionOptions = spamDetectionOptions.Value;
        _detectionMetrics = detectionMetrics;
    }

    private async Task<ContentDetectionConfig> GetConfigAsync(ContentCheckRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await _configRepository.GetEffectiveConfigAsync(request.Chat.Id, cancellationToken);
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
        activity?.SetTag("user_id", request.User.Id);
        activity?.SetTag("chat_id", request.Chat.Id);
        activity?.SetTag("engine_version", "v2");

        var startTimestamp = Stopwatch.GetTimestamp();

        // Load latest config from database
        var config = await GetConfigAsync(request, cancellationToken);

        // Phase 4.13: URL Pre-Filter - Check for hard-blocked domains FIRST
        if (!string.IsNullOrWhiteSpace(request.Message))
        {
            var hardBlock = await _preFilterService.CheckHardBlockAsync(request.Message, request.Chat, cancellationToken);

            if (hardBlock.ShouldBlock)
            {
                _logger.LogWarning(
                    "Hard block triggered for {User} in {Chat}: {Reason}",
                    request.User.ToLogDebug(), request.Chat.ToLogDebug(), hardBlock.Reason);

                var hardBlockResult = new ContentDetectionResult
                {
                    IsSpam = true,
                    HardBlock = hardBlock,
                    TotalScore = ContentDetectionConstants.MaxScore,
                    PrimaryReason = hardBlock.Reason ?? "Hard block policy violation",
                    RecommendedAction = DetectionAction.AutoBan,
                    RequiresAIConfirmation = false,
                    CheckResults =
                    [
                        new()
                        {
                            CheckName = CheckName.UrlBlocklist,
                            Score = ContentDetectionConstants.MaxScore,
                            Abstained = false,
                            Details = hardBlock.Reason ?? "Domain on hard block list"
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

        // AI veto exists to double-check text heuristics (stop words, similarity, spacing, etc.)
        // ImageSpam/VideoSpam already use OpenAI Vision API — vetoing them with text-only AI
        // is redundant and harmful (text veto sees blank content for image-only messages)
        var hasNonAISpam = pipelineResult.CheckResults
            .Any(r => r.Score > 0 && !r.Abstained && r.CheckName is not (CheckName.ImageSpam or CheckName.VideoSpam));
        var shouldRunAIVeto = hasNonAISpam
            && infrastructureEnabled  // Global kill switch
            && spamDetectionEnabled;  // Per-chat spam detection toggle

        _logger.LogDebug("AI veto decision: HasNonAISpam={HasNonAISpam}, TotalScore={TotalScore}, InfraEnabled={InfraEnabled}, SpamDetectionEnabled={SpamEnabled}, ShouldRun={ShouldRun}",
            hasNonAISpam, pipelineResult.TotalScore, infrastructureEnabled, spamDetectionEnabled, shouldRunAIVeto);

        if (shouldRunAIVeto)
        {
            // AI veto using V2 check with proper scoring
            var aiVetoCheck = _contentChecksV2.FirstOrDefault(check => check.CheckName == CheckName.OpenAI);
            if (aiVetoCheck != null)
            {
                // Fetch custom prompt from prompt_versions table (null = use default prompt)
                var activePrompt = await _promptVersionRepo.GetActiveVersionAsync(request.Chat.Id, cancellationToken);
                var systemPrompt = activePrompt?.PromptText;

                LogRunningAIVetoCheck(_logger, request.User.ToLogDebug(), systemPrompt != null);

                var vetoRequest = request with { HasSpamFlags = true };
                var checkRequest = BuildAIRequest(vetoRequest, config, spamFeatureConfig, systemPrompt, pipelineResult.OcrExtractedText, pipelineResult.VisionAnalysisText, cancellationToken);
                var vetoResultV2 = await aiVetoCheck.CheckAsync(checkRequest);

                List<ContentCheckResponseV2> updatedCheckResults = [.. pipelineResult.CheckResults, vetoResultV2];

                // AI abstained (API error, timeout, rate limit) - defer to pipeline verdict
                if (vetoResultV2.Abstained)
                {
                    _logger.LogWarning("AI veto abstained for {User} ({Details}), deferring to pipeline verdict (TotalScore={TotalScore})",
                        request.User.ToLogDebug(), vetoResultV2.Details, pipelineResult.TotalScore);

                    // Return pipeline result with AI check appended for visibility
                    var deferredResult = pipelineResult with { CheckResults = updatedCheckResults };
                    RecordDetectionMetrics(startTimestamp, deferredResult, activity);
                    return deferredResult;
                }

                // AI ran successfully and returned clean (score = 0.0, not abstained) - veto the spam detection
                if (vetoResultV2.Score == 0.0)
                {
                    LogAIVetoedSpamDetection(_logger, request.User.ToLogInfo());

                    // Record veto for each algorithm that was overridden
                    foreach (var check in pipelineResult.CheckResults.Where(r => r.Score > 0 && !r.Abstained))
                    {
                        _detectionMetrics.RecordVeto(check.CheckName.ToString());
                    }

                    var vetoedResult = CreateVetoedResult(updatedCheckResults, vetoResultV2);
                    RecordDetectionMetrics(startTimestamp, vetoedResult, activity);
                    return vetoedResult;
                }

                // AI confirmed spam - AI score is the sole authority for action determination
                // Pipeline scores served as a gate to trigger the veto; AI verdict drives the action
                LogAIConfirmedSpam(_logger, request.User.ToLogDebug(), vetoResultV2.Score);

                var confirmedResult = pipelineResult with
                {
                    CheckResults = updatedCheckResults,
                    IsSpam = true,
                    TotalScore = vetoResultV2.Score,
                    PrimaryReason = $"AI confirmed spam: {vetoResultV2.Details} (score: {vetoResultV2.Score:F1})",
                    RecommendedAction = DetermineActionFromScore(vetoResultV2.Score, config.AutoBanThreshold, config.ReviewQueueThreshold),
                    RequiresAIConfirmation = false
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
                _logger.LogError(ex, "Error running {CheckName} for {User}", check.CheckName, request.User.ToLogDebug());
                // Continue with other checks
            }
        }

        // Extract OCR text and Vision analysis from ImageSpam check for downstream veto use
        var imageSpamResponse = checkResponsesV2.FirstOrDefault(r => r.CheckName == CheckName.ImageSpam);
        var ocrExtractedText = imageSpamResponse?.OcrExtractedText;
        var visionAnalysisText = imageSpamResponse?.VisionAnalysisText;

        // Determine action based on total score vs per-chat config thresholds
        // Pipeline results cap at ReviewQueue — AutoBan requires AI confirmation to guard false positives
        var isSpam = totalScore >= config.ReviewQueueThreshold;
        var recommendedAction = totalScore >= config.ReviewQueueThreshold
            ? DetectionAction.ReviewQueue
            : DetectionAction.Allow;

        // AI confirmation runs on any spam signal to reduce false positives
        // (actual execution happens in CheckMessageAsync based on config.AIVeto.Enabled)
        var requiresAIConfirmation = totalScore > 0;

        var primaryReason = totalScore >= config.ReviewQueueThreshold
            ? $"Additive score: {totalScore:F1} points (review threshold: {config.ReviewQueueThreshold:F1})"
            : "No spam detected";

        var result = new ContentDetectionResult
        {
            IsSpam = isSpam,
            TotalScore = totalScore,
            CheckResults = checkResponsesV2,
            PrimaryReason = primaryReason,
            RecommendedAction = recommendedAction,
            RequiresAIConfirmation = requiresAIConfirmation,
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
            CheckName.ChannelReply => config.ChannelReply.Enabled && request.Metadata.IsReplyToChannelPost,
            _ => false
        };

        if (!enabled)
            return false;

        // Keep in sync with enabled switch above (CAS, FileScanning, OpenAI use separate paths)
        var alwaysRun = check.CheckName switch
        {
            CheckName.StopWords => config.StopWords.AlwaysRun,
            CheckName.Bayes => config.Bayes.AlwaysRun,
            CheckName.Similarity => config.Similarity.AlwaysRun,
            CheckName.Spacing => config.Spacing.AlwaysRun,
            CheckName.InvisibleChars => config.InvisibleChars.AlwaysRun,
            CheckName.ThreatIntel => config.ThreatIntel.AlwaysRun,
            CheckName.UrlBlocklist => config.UrlBlocklist.AlwaysRun,
            CheckName.ImageSpam => config.ImageSpam.AlwaysRun,
            CheckName.VideoSpam => config.VideoSpam.AlwaysRun,
            CheckName.ChannelReply => config.ChannelReply.AlwaysRun,
            _ => false
        };

        if (alwaysRun)
            return true;

        return check.ShouldExecute(request);
    }

    private ContentCheckRequestBase BuildRequestForCheckV2(
        IContentCheckV2 check,
        ContentCheckRequest originalRequest,
        ContentDetectionConfig config,
        CancellationToken cancellationToken)
    {
        return check.CheckName switch
        {
            CheckName.StopWords => new StopWordsCheckRequest
            {
                Message = originalRequest.Message ?? "",
                User = originalRequest.User,
                Chat = originalRequest.Chat,
                CancellationToken = cancellationToken
            },

            CheckName.Bayes => new BayesCheckRequest
            {
                Message = originalRequest.Message ?? "",
                User = originalRequest.User,
                Chat = originalRequest.Chat,
                MinMessageLength = config.MinMessageLength,
                CancellationToken = cancellationToken
            },

            // CAS moved to user join flow (WelcomeService) - checks USER not MESSAGE

            CheckName.Similarity => new SimilarityCheckRequest
            {
                Message = originalRequest.Message ?? "",
                User = originalRequest.User,
                Chat = originalRequest.Chat,
                MinMessageLength = config.MinMessageLength,
                SimilarityThreshold = config.Similarity.Threshold,
                CancellationToken = cancellationToken
            },

            CheckName.Spacing => new SpacingCheckRequest
            {
                Message = originalRequest.Message ?? "",
                User = originalRequest.User,
                Chat = originalRequest.Chat,
                SuspiciousRatioThreshold = config.Spacing.ShortWordRatioThreshold,
                ShortWordLength = config.Spacing.ShortWordLength,
                MinWordsCount = config.Spacing.MinWordsCount,
                CancellationToken = cancellationToken
            },

            CheckName.InvisibleChars => new InvisibleCharsCheckRequest
            {
                Message = originalRequest.Message ?? "",
                User = originalRequest.User,
                Chat = originalRequest.Chat,
                CancellationToken = cancellationToken
            },

            CheckName.ThreatIntel => new ThreatIntelCheckRequest
            {
                Message = originalRequest.Message ?? "",
                User = originalRequest.User,
                Chat = originalRequest.Chat,
                Urls = originalRequest.Urls ?? [],
                VirusTotalApiKey = _spamDetectionOptions.ApiKey,
                CancellationToken = cancellationToken
            },

            CheckName.UrlBlocklist => new UrlBlocklistCheckRequest
            {
                Message = originalRequest.Message ?? "",
                User = originalRequest.User,
                Chat = originalRequest.Chat,
                Urls = originalRequest.Urls ?? [],
                CancellationToken = cancellationToken
            },

            CheckName.ImageSpam => new ImageCheckRequest
            {
                Message = originalRequest.Message ?? "",
                User = originalRequest.User,
                Chat = originalRequest.Chat,
                PhotoFileId = originalRequest.PhotoFileId ?? "",
                PhotoLocalPath = originalRequest.PhotoLocalPath,
                CustomPrompt = null,
                CancellationToken = cancellationToken
            },

            CheckName.VideoSpam => new VideoCheckRequest
            {
                Message = originalRequest.Message ?? "",
                User = originalRequest.User,
                Chat = originalRequest.Chat,
                VideoLocalPath = originalRequest.VideoLocalPath ?? "",
                CustomPrompt = null,
                CancellationToken = cancellationToken
            },

            CheckName.ChannelReply => new ChannelReplyCheckRequest
            {
                Message = originalRequest.Message ?? "",
                User = originalRequest.User,
                Chat = originalRequest.Chat,
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
            User = originalRequest.User,
            Chat = originalRequest.Chat,
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
        var resultType = result.Abstained ? "abstained" : (result.Score >= 1.0 ? "spam" : "clean");
        _detectionMetrics.RecordSpamDetection(check.CheckName.ToString(), resultType, durationMs);

        if (activity != null)
        {
            activity.SetTag("spam_detection.score", result.Score);
            activity.SetTag("spam_detection.abstained", result.Abstained);
            activity.SetTag("spam_detection.duration_ms", durationMs);
        }

        return result;
    }

    private static DetectionAction DetermineActionFromScore(double totalScore, double autoBanThreshold, double reviewQueueThreshold)
    {
        if (totalScore >= autoBanThreshold)
            return DetectionAction.AutoBan;

        if (totalScore >= reviewQueueThreshold)
            return DetectionAction.ReviewQueue;

        return DetectionAction.Allow;
    }

    private ContentDetectionResult CreateVetoedResult(List<ContentCheckResponseV2> checkResults, ContentCheckResponseV2 vetoResult)
    {
        return new ContentDetectionResult
        {
            IsSpam = false,
            TotalScore = 0,
            CheckResults = checkResults,
            PrimaryReason = vetoResult.Details,
            RecommendedAction = DetectionAction.Allow,
            RequiresAIConfirmation = false
        };
    }

    private void RecordDetectionMetrics(long startTimestamp, ContentDetectionResult result, Activity? activity)
    {
        var durationMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

        var resultType = result.IsSpam ? "spam" : "clean";
        _detectionMetrics.RecordSpamDetection("pipeline", resultType, durationMs);

        if (activity != null)
        {
            activity.SetTag("spam_detection.is_spam", result.IsSpam);
            activity.SetTag("spam_detection.total_score", result.TotalScore);
            activity.SetTag("spam_detection.checks_run", result.CheckResults.Count);
            activity.SetTag("spam_detection.duration_ms", durationMs);
        }
    }
}
