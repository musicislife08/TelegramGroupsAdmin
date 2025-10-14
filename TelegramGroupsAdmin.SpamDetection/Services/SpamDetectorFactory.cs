using System.Text.Json;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.SpamDetection.Abstractions;
using TelegramGroupsAdmin.SpamDetection.Configuration;
using TelegramGroupsAdmin.SpamDetection.Models;
using TelegramGroupsAdmin.SpamDetection.Repositories;

namespace TelegramGroupsAdmin.SpamDetection.Services;

/// <summary>
/// Factory implementation that orchestrates all spam detection checks
/// </summary>
public class SpamDetectorFactory : ISpamDetectorFactory
{
    private readonly ILogger<SpamDetectorFactory> _logger;
    private readonly ISpamDetectionConfigRepository _configRepository;
    private readonly IEnumerable<ISpamCheck> _spamChecks;
    private readonly IOpenAITranslationService _translationService;

    public SpamDetectorFactory(
        ILogger<SpamDetectorFactory> logger,
        ISpamDetectionConfigRepository configRepository,
        IEnumerable<ISpamCheck> spamChecks,
        IOpenAITranslationService translationService)
    {
        _logger = logger;
        _configRepository = configRepository;
        _spamChecks = spamChecks;
        _translationService = translationService;
    }

    private async Task<SpamDetectionConfig> GetConfigAsync(SpamCheckRequest request, CancellationToken cancellationToken)
    {
        try
        {
            // Use per-chat config if ChatId is provided, otherwise use global
            if (!string.IsNullOrEmpty(request.ChatId))
            {
                return await _configRepository.GetChatConfigAsync(request.ChatId, cancellationToken);
            }

            return await _configRepository.GetGlobalConfigAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load spam detection config, using default");
            return new SpamDetectionConfig();
        }
    }

    /// <summary>
    /// Run all applicable spam checks on a message and return aggregated results
    /// </summary>
    public async Task<SpamDetectionResult> CheckMessageAsync(SpamCheckRequest request, CancellationToken cancellationToken = default)
    {
        // Load latest config from database (per-chat or global)
        var config = await GetConfigAsync(request, cancellationToken);

        var checkResults = new List<SpamCheckResponse>();

        _logger.LogDebug("Starting spam detection for user {UserId} in chat {ChatId}", request.UserId, request.ChatId);

        // First, run all non-OpenAI checks
        var nonOpenAIResult = await CheckMessageWithoutOpenAIAsync(request, cancellationToken);
        checkResults.AddRange(nonOpenAIResult.CheckResults);

        // Determine if we should run OpenAI veto check
        var shouldRunOpenAI = nonOpenAIResult.ShouldVeto && config.OpenAI.Enabled && config.OpenAI.VetoMode;

        if (shouldRunOpenAI)
        {
            var openAICheck = _spamChecks.FirstOrDefault(check => check.CheckName == "OpenAI");
            if (openAICheck != null)
            {
                _logger.LogDebug("Running OpenAI veto check for user {UserId}", request.UserId);

                // Update request to indicate other checks found spam
                var vetoRequest = request with { HasSpamFlags = true };

                if (openAICheck.ShouldExecute(vetoRequest))
                {
                    var vetoResult = await openAICheck.CheckAsync(vetoRequest, cancellationToken);
                    checkResults.Add(vetoResult);

                    // If OpenAI vetoes the spam detection (says it's not spam), override the result
                    if (!vetoResult.IsSpam)
                    {
                        _logger.LogInformation("OpenAI vetoed spam detection for user {UserId} with {Confidence}% confidence",
                            request.UserId, vetoResult.Confidence);
                        return CreateVetoedResult(checkResults, vetoResult);
                    }
                }
            }
        }

        return AggregateResults(checkResults, config);
    }

    /// <summary>
    /// Run only non-OpenAI checks to determine if message should be vetoed by OpenAI
    /// </summary>
    public async Task<SpamDetectionResult> CheckMessageWithoutOpenAIAsync(SpamCheckRequest request, CancellationToken cancellationToken = default)
    {
        // Load latest config from database (per-chat or global)
        var config = await GetConfigAsync(request, cancellationToken);

        var checkResults = new List<SpamCheckResponse>();

        // Run all checks in two phases:
        // Phase 1: Run InvisibleChars check on ORIGINAL message (before translation)
        // Phase 2: Translate, then run all other checks on translated message

        var invisibleCharsCheck = _spamChecks.FirstOrDefault(check => check.CheckName == "InvisibleChars");
        if (invisibleCharsCheck != null && invisibleCharsCheck.ShouldExecute(request))
        {
            try
            {
                _logger.LogDebug("Running InvisibleChars on original message for user {UserId}", request.UserId);
                var result = await invisibleCharsCheck.CheckAsync(request, cancellationToken);
                checkResults.Add(result);
                _logger.LogDebug("InvisibleChars result: IsSpam={IsSpam}, Confidence={Confidence}",
                    result.IsSpam, result.Confidence);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error running InvisibleChars for user {UserId}", request.UserId);
            }
        }

        // Preprocess: Translate foreign language if needed
        var processedRequest = await PreprocessMessageAsync(request, config, cancellationToken);

        // Run all other checks on potentially translated message
        var checks = _spamChecks.Where(check => check.CheckName != "OpenAI" && check.CheckName != "InvisibleChars").ToList();

        foreach (var check in checks)
        {
            if (!check.ShouldExecute(processedRequest))
            {
                _logger.LogDebug("Skipping {CheckName} for user {UserId} - conditions not met", check.CheckName, processedRequest.UserId);
                continue;
            }

            try
            {
                _logger.LogDebug("Running {CheckName} for user {UserId}", check.CheckName, processedRequest.UserId);
                var result = await check.CheckAsync(processedRequest, cancellationToken);
                checkResults.Add(result);

                _logger.LogDebug("{CheckName} result: IsSpam={IsSpam}, Confidence={Confidence}",
                    check.CheckName, result.IsSpam, result.Confidence);
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
    /// </summary>
    private int CalculateNetConfidence(List<SpamCheckResponse> checkResults)
    {
        var spamVotes = 0;
        var hamVotes = 0;

        foreach (var check in checkResults)
        {
            if (check.IsSpam)
            {
                spamVotes += check.Confidence;
            }
            else
            {
                hamVotes += check.Confidence;
            }
        }

        var netConfidence = spamVotes - hamVotes;

        _logger.LogDebug("Net confidence: {Net} (spam votes: {SpamVotes}, ham votes: {HamVotes})",
            netConfidence, spamVotes, hamVotes);

        return netConfidence;
    }

    /// <summary>
    /// Aggregate results from multiple spam checks
    /// Phase 2.6: Uses weighted voting (net confidence) for two-tier decision system
    /// </summary>
    private SpamDetectionResult AggregateResults(List<SpamCheckResponse> checkResults, SpamDetectionConfig config)
    {
        var spamResults = checkResults.Where(r => r.IsSpam).ToList();
        var isSpam = spamResults.Any();
        var spamFlags = spamResults.Count;

        var maxConfidence = isSpam ? spamResults.Max(r => r.Confidence) : 0;
        var avgConfidence = isSpam ? (int)spamResults.Average(r => r.Confidence) : 0;

        // Phase 2.6: Calculate net confidence using weighted voting
        var netConfidence = CalculateNetConfidence(checkResults);

        // Phase 2.6: Two-tier decision system based on net confidence
        // Net > +50: Run OpenAI veto (safety before ban)
        // Net ≤ +50: Admin review queue (skip OpenAI cost)
        // Net < 0: Allow (no spam detected)
        var shouldVeto = netConfidence > 50 && config.OpenAI.VetoMode;

        // Determine recommended action based on net confidence
        var recommendedAction = DetermineActionFromNetConfidence(netConfidence, config);

        // Primary reason is from the highest confidence check
        var primaryReason = isSpam
            ? spamResults.OrderByDescending(r => r.Confidence).First().Details
            : "No spam detected";

        var result = new SpamDetectionResult
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

        _logger.LogDebug("Aggregated result: IsSpam={IsSpam}, NetConfidence={NetConfidence}, MaxConfidence={MaxConfidence}, SpamFlags={SpamFlags}, Action={Action}",
            result.IsSpam, result.NetConfidence, result.MaxConfidence, result.SpamFlags, result.RecommendedAction);

        return result;
    }

    /// <summary>
    /// Create result when OpenAI vetoes spam detection
    /// </summary>
    private SpamDetectionResult CreateVetoedResult(List<SpamCheckResponse> checkResults, SpamCheckResponse vetoResult)
    {
        // Calculate net confidence even for vetoed results (for analytics)
        var netConfidence = CalculateNetConfidence(checkResults);

        return new SpamDetectionResult
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
    /// Determine recommended action based on confidence score (legacy method)
    /// </summary>
    private SpamAction DetermineAction(int confidence, SpamDetectionConfig config)
    {
        if (confidence >= config.AutoBanThreshold)
        {
            return SpamAction.AutoBan;
        }
        if (confidence >= config.ReviewQueueThreshold)
        {
            return SpamAction.ReviewQueue;
        }
        return SpamAction.Allow;
    }

    /// <summary>
    /// Phase 2.6: Determine recommended action based on net confidence (weighted voting)
    /// Net > +50: Run OpenAI veto (safety before ban)
    /// Net ≤ +50 AND > 0: Admin review queue (skip OpenAI cost)
    /// Net ≤ 0: Allow (no spam detected)
    /// </summary>
    private SpamAction DetermineActionFromNetConfidence(int netConfidence, SpamDetectionConfig config)
    {
        if (netConfidence > 50)
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
    private async Task<SpamCheckRequest> PreprocessMessageAsync(
        SpamCheckRequest request,
        SpamDetectionConfig config,
        CancellationToken cancellationToken)
    {
        // Translate foreign language to English if enabled
        if (config.Translation.Enabled && !string.IsNullOrWhiteSpace(request.Message) && request.Message.Length >= 20)
        {
            // Quick check: if message is mostly Latin script, likely English - skip expensive OpenAI translation
            if (IsLikelyLatinScript(request.Message))
            {
                _logger.LogDebug("Message is primarily Latin script for user {UserId} - skipping translation", request.UserId);
            }
            else
            {
                try
                {
                    var translationResult = await _translationService.TranslateToEnglishAsync(request.Message, cancellationToken);

                    if (translationResult?.WasTranslated == true)
                    {
                        _logger.LogInformation("Translated {Language} message to English for user {UserId}",
                            translationResult.DetectedLanguage, request.UserId);

                        // Return request with translated text - all subsequent checks will use this
                        return request with { Message = translationResult.TranslatedText };
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Translation failed for user {UserId} - continuing with original text", request.UserId);
                    // Continue with original text on translation failure
                }
            }
        }

        // No translation needed or failed - return original request
        return request;
    }


    /// <summary>
    /// Check if message is primarily Latin/ASCII script (likely English) to avoid unnecessary OpenAI translation
    /// </summary>
    private static bool IsLikelyLatinScript(string message)
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

        // If >80% of letters are Latin script, likely English/Western European language
        return (double)latinCount / letterCount > 0.8;
    }

    /// <summary>
    /// Phase 2.6: Serialize check results to JSON for storage in detection_results.check_results
    /// Returns compact JSON with minimal field names to save space
    /// </summary>
    public static string SerializeCheckResults(List<SpamCheckResponse> checkResults)
    {
        var checks = checkResults.Select(c => new
        {
            name = c.CheckName,
            spam = c.IsSpam,
            conf = c.Confidence,
            reason = c.Details
        });

        return JsonSerializer.Serialize(new { checks }, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false // Compact JSON
        });
    }
}