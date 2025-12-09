using System.Text.Json;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Configuration.Models;

namespace TelegramGroupsAdmin.Core.Services.AI;

/// <summary>
/// AI-based translation service for detecting and translating foreign language content
/// Uses Semantic Kernel for multi-provider support (OpenAI, Azure OpenAI, local models)
/// </summary>
public class AITranslationService : IAITranslationService
{
    private readonly IAIServiceFactory _aiServiceFactory;
    private readonly ILogger<AITranslationService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public AITranslationService(
        IAIServiceFactory aiServiceFactory,
        ILogger<AITranslationService> logger)
    {
        _aiServiceFactory = aiServiceFactory;
        _logger = logger;
    }

    /// <summary>
    /// Translate text to English if it's in a foreign language
    /// </summary>
    public async Task<TranslationResult?> TranslateToEnglishAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        try
        {
            // Get chat service for translation feature
            var chatService = await _aiServiceFactory.GetChatServiceAsync(AIFeatureType.Translation, cancellationToken);
            if (chatService == null)
            {
                _logger.LogDebug("AI Translation service not configured, skipping translation");
                return null;
            }

            var systemPrompt = """
                You are a language detection and translation assistant.
                Analyze text and respond with a JSON object.
                Always respond with ONLY raw JSON, no markdown code blocks.
                """;

            var userPrompt = $"""
                Analyze this text and respond with a JSON object containing:
                1. 'language': The detected language (e.g., 'english', 'russian', 'chinese', etc.)
                2. 'translation': If the language is NOT English, provide an English translation. If it's already English, return the original text.
                3. 'confidence': Your confidence level (0.0 to 1.0) in the language detection

                Text to analyze: "{text}"

                IMPORTANT: Respond with ONLY the raw JSON object. Do NOT wrap it in markdown code blocks or backticks.
                """;

            var featureConfig = await _aiServiceFactory.GetFeatureConfigAsync(AIFeatureType.Translation, cancellationToken);
            var options = new ChatCompletionOptions
            {
                MaxTokens = Math.Min(featureConfig?.MaxTokens ?? 200, 200), // Cap at 200 for translation
                Temperature = featureConfig?.Temperature ?? 0.2,
                JsonMode = true
            };

            var result = await chatService.GetCompletionAsync(systemPrompt, userPrompt, options, cancellationToken);
            if (result == null || string.IsNullOrWhiteSpace(result.Content))
            {
                _logger.LogWarning("Empty response from AI translation service");
                return null;
            }

            var content = result.Content.Trim();

            // Remove markdown code blocks if present (some models still add them despite JsonMode)
            if (content.StartsWith("```"))
            {
                var lines = content.Split('\n');
                content = string.Join('\n', lines.Skip(1).SkipLast(1)).Trim();
            }

            // Parse the JSON response
            var translationResult = JsonSerializer.Deserialize<TranslationApiResult>(content, JsonOptions);
            if (translationResult == null)
            {
                _logger.LogWarning("Failed to parse AI translation result: {Content}", content);
                return null;
            }

            var isEnglish = translationResult.Language.Equals("english", StringComparison.OrdinalIgnoreCase);

            return new TranslationResult
            {
                TranslatedText = translationResult.Translation ?? text,
                DetectedLanguage = translationResult.Language,
                WasTranslated = !isEnglish && !string.IsNullOrEmpty(translationResult.Translation)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to translate text using AI (text length: {Length} chars)", text.Length);
            return null;
        }
    }

    /// <summary>
    /// Structure for parsing AI's translation result
    /// </summary>
    private record TranslationApiResult
    {
        public string Language { get; init; } = string.Empty;
        public string? Translation { get; init; }
        public double Confidence { get; init; }
    }
}
