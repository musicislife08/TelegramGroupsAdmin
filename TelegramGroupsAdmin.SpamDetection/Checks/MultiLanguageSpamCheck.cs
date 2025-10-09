using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.SpamDetection.Abstractions;
using TelegramGroupsAdmin.SpamDetection.Configuration;
using TelegramGroupsAdmin.SpamDetection.Models;
using TelegramGroupsAdmin.SpamDetection.Services;

namespace TelegramGroupsAdmin.SpamDetection.Checks;

/// <summary>
/// Enhanced spam check that detects foreign language spam using translation
/// Improved approach using OpenAI translation instead of complex script analysis
/// </summary>
public class MultiLanguageSpamCheck : ISpamCheck
{
    private readonly ILogger<MultiLanguageSpamCheck> _logger;
    private readonly SpamDetectionConfig _config;
    private readonly IOpenAITranslationService _translationService;
    private readonly ISpamCheck[] _spamChecks;

    public string CheckName => "MultiLanguage";

    public MultiLanguageSpamCheck(
        ILogger<MultiLanguageSpamCheck> logger,
        SpamDetectionConfig config,
        IOpenAITranslationService translationService,
        IEnumerable<ISpamCheck> spamChecks)
    {
        _logger = logger;
        _config = config;
        _translationService = translationService;
        // Filter out MultiLanguage check to avoid infinite recursion
        _spamChecks = spamChecks.Where(check => check.CheckName != "MultiLanguage").ToArray();
    }

    /// <summary>
    /// Check if multi-language check should be executed
    /// </summary>
    public bool ShouldExecute(SpamCheckRequest request)
    {
        // Check if multi-language check is enabled
        if (!_config.MultiLanguage.Enabled)
        {
            return false;
        }

        // Skip empty or very short messages
        if (string.IsNullOrWhiteSpace(request.Message) || request.Message.Length < 20)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Execute translation-based multi-language spam check
    /// </summary>
    public async Task<SpamCheckResponse> CheckAsync(SpamCheckRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            // First check for invisible characters (still useful)
            var invisibleChars = CountInvisibleCharacters(request.Message);
            if (invisibleChars > 0)
            {
                _logger.LogDebug("MultiLanguage check for user {UserId}: Found {InvisibleChars} invisible characters",
                    request.UserId, invisibleChars);

                return new SpamCheckResponse
                {
                    CheckName = CheckName,
                    IsSpam = true,
                    Details = $"Contains {invisibleChars} invisible/hidden characters",
                    Confidence = 90
                };
            }

            // Attempt translation
            var translationResult = await _translationService.TranslateToEnglishAsync(request.Message, cancellationToken);

            if (translationResult == null)
            {
                // Translation failed - assume it's English
                return new SpamCheckResponse
                {
                    CheckName = CheckName,
                    IsSpam = false,
                    Details = "Unable to detect language (assuming English)",
                    Confidence = 0
                };
            }

            if (!translationResult.WasTranslated)
            {
                // Text is already in English
                return new SpamCheckResponse
                {
                    CheckName = CheckName,
                    IsSpam = false,
                    Details = $"Text detected as {translationResult.DetectedLanguage}",
                    Confidence = 0
                };
            }

            // Text was in foreign language - run spam checks on translated version
            _logger.LogDebug("MultiLanguage check for user {UserId}: Translated {Language} to English",
                request.UserId, translationResult.DetectedLanguage);

            var translatedRequest = request with { Message = translationResult.TranslatedText };
            var spamResults = new List<SpamCheckResponse>();

            // Run key spam checks on translated text
            foreach (var check in _spamChecks)
            {
                if (check.ShouldExecute(translatedRequest))
                {
                    var result = await check.CheckAsync(translatedRequest, cancellationToken);
                    if (result.IsSpam)
                    {
                        spamResults.Add(result);
                    }
                }
            }

            // Determine if translated content is spam
            var hasSpamResults = spamResults.Any();
            var maxConfidence = hasSpamResults ? spamResults.Max(r => r.Confidence) : 0;
            var spamReasons = hasSpamResults ? string.Join(", ", spamResults.Select(r => r.CheckName)) : "none";

            var details = $"Translated {translationResult.DetectedLanguage} text" +
                         (hasSpamResults ? $" - spam detected by: {spamReasons}" : " - no spam detected");

            return new SpamCheckResponse
            {
                CheckName = CheckName,
                IsSpam = hasSpamResults,
                Details = details,
                Confidence = hasSpamResults ? Math.Min(maxConfidence + 10, 100) : 0 // Slight boost for foreign language spam
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MultiLanguage check failed for user {UserId}", request.UserId);
            return new SpamCheckResponse
            {
                CheckName = CheckName,
                IsSpam = false, // Fail open
                Details = "MultiLanguage check failed due to error",
                Confidence = 0,
                Error = ex
            };
        }
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


}

