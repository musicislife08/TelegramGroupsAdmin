using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TelegramGroupsAdmin.Configuration;

namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// OpenAI-based translation service for detecting and translating foreign language spam
/// </summary>
public class OpenAITranslationService : IOpenAITranslationService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenAITranslationService> _logger;
    private readonly OpenAIOptions _options;

    public OpenAITranslationService(
        IHttpClientFactory httpClientFactory,
        ILogger<OpenAITranslationService> logger,
        IOptions<OpenAIOptions> options)
    {
        _httpClient = httpClientFactory.CreateClient();
        _logger = logger;
        _options = options.Value;

        // Configure OpenAI client
        if (!string.IsNullOrEmpty(_options.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_options.ApiKey}");
        }

        _httpClient.Timeout = TimeSpan.FromSeconds(15);
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
            if (string.IsNullOrEmpty(_options.ApiKey))
            {
                _logger.LogDebug("OpenAI API key not configured, skipping translation");
                return null;
            }

            var prompt = $@"Analyze this text and respond with a JSON object containing:
1. 'language': The detected language (e.g., 'english', 'russian', 'chinese', etc.)
2. 'translation': If the language is NOT English, provide an English translation. If it's already English, return the original text.
3. 'confidence': Your confidence level (0.0 to 1.0) in the language detection

Text to analyze: ""{text}""

IMPORTANT: Respond with ONLY the raw JSON object. Do NOT wrap it in markdown code blocks or backticks.";

            var request = new
            {
                model = "gpt-4o-mini",
                messages = new[]
                {
                    new { role = "user", content = prompt }
                },
                max_tokens = 200,
                temperature = 0.1
            };

            var response = await _httpClient.PostAsJsonAsync("https://api.openai.com/v1/chat/completions", request, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("OpenAI translation request failed with status {StatusCode}", response.StatusCode);
                return null;
            }

            var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var openAiResponse = JsonSerializer.Deserialize<OpenAIResponse>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            var firstChoice = openAiResponse?.Choices?.FirstOrDefault();
            if (firstChoice?.Message?.Content == null)
            {
                _logger.LogWarning("Invalid OpenAI response format for translation");
                return null;
            }

            var content = firstChoice.Message.Content.Trim();
            if (string.IsNullOrEmpty(content))
            {
                _logger.LogWarning("Empty OpenAI response content for translation");
                return null;
            }

            // Remove markdown code blocks if present (e.g., ```json\n...\n```)
            if (content.StartsWith("```"))
            {
                var lines = content.Split('\n');
                content = string.Join('\n', lines.Skip(1).SkipLast(1)).Trim();
            }

            // Parse the JSON response
            var result = JsonSerializer.Deserialize<TranslationApiResult>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result == null)
            {
                _logger.LogWarning("Failed to parse OpenAI translation result: {Content}", content);
                return null;
            }

            var isEnglish = result.Language.Equals("english", StringComparison.OrdinalIgnoreCase);

            return new TranslationResult
            {
                TranslatedText = result.Translation ?? text,
                DetectedLanguage = result.Language,
                WasTranslated = !isEnglish && !string.IsNullOrEmpty(result.Translation)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to translate text using OpenAI");
            return null;
        }
    }

    /// <summary>
    /// OpenAI API response structure
    /// </summary>
    private record OpenAIResponse
    {
        public OpenAIChoice[]? Choices { get; init; }
    }

    private record OpenAIChoice
    {
        public OpenAIMessage? Message { get; init; }
    }

    private record OpenAIMessage
    {
        public string? Content { get; init; }
    }

    /// <summary>
    /// Structure for parsing OpenAI's translation result
    /// </summary>
    private record TranslationApiResult
    {
        public string Language { get; init; } = string.Empty;
        public string? Translation { get; init; }
        public double Confidence { get; init; }
    }
}