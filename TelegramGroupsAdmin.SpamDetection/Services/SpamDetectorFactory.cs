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

    private async Task<SpamDetectionConfig> GetConfigAsync(CancellationToken cancellationToken)
    {
        try
        {
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
        // Load latest config from database
        var config = await GetConfigAsync(cancellationToken);

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

                    // If OpenAI vetoes the spam detection, override the result
                    if (!vetoResult.IsSpam && vetoResult.Confidence == 0)
                    {
                        _logger.LogInformation("OpenAI vetoed spam detection for user {UserId}", request.UserId);
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
        // Load latest config from database
        var config = await GetConfigAsync(cancellationToken);

        var checkResults = new List<SpamCheckResponse>();

        // Preprocess: Check for invisible characters and translate if needed
        var (processedRequest, invisibleCharResult) = await PreprocessMessageAsync(request, config, cancellationToken);

        // Add invisible character check result if applicable
        if (invisibleCharResult != null)
        {
            checkResults.Add(invisibleCharResult);
        }

        // Run all checks except OpenAI (MultiLanguage is already handled in preprocessing)
        var checks = _spamChecks.Where(check => check.CheckName != "OpenAI" && check.CheckName != "MultiLanguage").ToList();

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
    /// Aggregate results from multiple spam checks
    /// </summary>
    private SpamDetectionResult AggregateResults(List<SpamCheckResponse> checkResults, SpamDetectionConfig config)
    {
        var spamResults = checkResults.Where(r => r.IsSpam).ToList();
        var isSpam = spamResults.Any();
        var spamFlags = spamResults.Count;

        var maxConfidence = isSpam ? spamResults.Max(r => r.Confidence) : 0;
        var avgConfidence = isSpam ? (int)spamResults.Average(r => r.Confidence) : 0;

        // Determine recommended action based on confidence thresholds
        var recommendedAction = DetermineAction(maxConfidence, config);

        // Primary reason is from the highest confidence check
        var primaryReason = isSpam
            ? spamResults.OrderByDescending(r => r.Confidence).First().Details
            : "No spam detected";

        // Should veto if spam detected but confidence is not extremely high
        var shouldVeto = isSpam && maxConfidence < 95 && config.OpenAI.VetoMode;

        var result = new SpamDetectionResult
        {
            IsSpam = isSpam,
            MaxConfidence = maxConfidence,
            AvgConfidence = avgConfidence,
            SpamFlags = spamFlags,
            CheckResults = checkResults,
            PrimaryReason = primaryReason,
            RecommendedAction = recommendedAction,
            ShouldVeto = shouldVeto
        };

        _logger.LogDebug("Aggregated result: IsSpam={IsSpam}, MaxConfidence={MaxConfidence}, SpamFlags={SpamFlags}, Action={Action}",
            result.IsSpam, result.MaxConfidence, result.SpamFlags, result.RecommendedAction);

        return result;
    }

    /// <summary>
    /// Create result when OpenAI vetoes spam detection
    /// </summary>
    private SpamDetectionResult CreateVetoedResult(List<SpamCheckResponse> checkResults, SpamCheckResponse vetoResult)
    {
        return new SpamDetectionResult
        {
            IsSpam = false, // Vetoed
            MaxConfidence = 0,
            AvgConfidence = 0,
            SpamFlags = 0,
            CheckResults = checkResults,
            PrimaryReason = vetoResult.Details,
            RecommendedAction = SpamAction.Allow,
            ShouldVeto = false
        };
    }

    /// <summary>
    /// Determine recommended action based on confidence score
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
    /// Preprocess message: check for invisible characters and translate if foreign language
    /// </summary>
    /// <returns>Tuple of (processed request with potentially translated text, invisible char check result if applicable)</returns>
    private async Task<(SpamCheckRequest processedRequest, SpamCheckResponse? invisibleCharResult)> PreprocessMessageAsync(
        SpamCheckRequest request,
        SpamDetectionConfig config,
        CancellationToken cancellationToken)
    {
        SpamCheckResponse? invisibleCharResult = null;

        // Check for invisible characters first (immediate spam indicator)
        if (config.MultiLanguage.Enabled && !string.IsNullOrWhiteSpace(request.Message))
        {
            var invisibleChars = CountInvisibleCharacters(request.Message);
            if (invisibleChars > 0)
            {
                _logger.LogWarning("Detected {Count} invisible characters in message from user {UserId}", invisibleChars, request.UserId);
                invisibleCharResult = new SpamCheckResponse
                {
                    CheckName = "InvisibleChars",
                    IsSpam = true,
                    Details = $"Contains {invisibleChars} invisible/hidden characters",
                    Confidence = 90
                };
                // Still continue with translation in case there's legitimate foreign text + invisible chars
            }
        }

        // Translate foreign language to English if enabled
        if (config.MultiLanguage.Enabled && !string.IsNullOrWhiteSpace(request.Message) && request.Message.Length >= 20)
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
                        return (request with { Message = translationResult.TranslatedText }, invisibleCharResult);
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
        return (request, invisibleCharResult);
    }

    /// <summary>
    /// Count invisible/zero-width characters that might be used to hide spam
    /// </summary>
    private static int CountInvisibleCharacters(string message)
    {
        var invisibleChars = new[]
        {
            '\u200B', // Zero Width Space
            '\u200C', // Zero Width Non-Joiner
            '\u200D', // Zero Width Joiner
            '\u2060', // Word Joiner
            '\uFEFF'  // Zero Width No-Break Space
        };

        return message.Count(c => invisibleChars.Contains(c));
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
}