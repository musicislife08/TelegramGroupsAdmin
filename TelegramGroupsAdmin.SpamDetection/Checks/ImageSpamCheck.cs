using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.SpamDetection.Abstractions;
using TelegramGroupsAdmin.SpamDetection.Configuration;
using TelegramGroupsAdmin.SpamDetection.Models;
using TelegramGroupsAdmin.SpamDetection.Repositories;

namespace TelegramGroupsAdmin.SpamDetection.Checks;

/// <summary>
/// Spam check that analyzes images using OpenAI Vision API for spam detection
/// Based on existing OpenAIVisionSpamDetectionService implementation
/// </summary>
public class ImageSpamCheck : ISpamCheck
{
    private readonly ILogger<ImageSpamCheck> _logger;
    private readonly ISpamDetectionConfigRepository _configRepository;
    private readonly OpenAIOptions _openAIOptions;
    private readonly HttpClient _httpClient;

    public string CheckName => "ImageSpam";

    public ImageSpamCheck(
        ILogger<ImageSpamCheck> logger,
        ISpamDetectionConfigRepository configRepository,
        IOptions<OpenAIOptions> openAIOptions,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _configRepository = configRepository;
        _openAIOptions = openAIOptions.Value;
        _httpClient = httpClientFactory.CreateClient();

        // Configure HTTP client - will be updated from config in CheckAsync
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Check if image spam check should be executed
    /// </summary>
    public bool ShouldExecute(SpamCheckRequest request)
    {
        // Only run if image data is provided
        return request.ImageData != null;
    }

    /// <summary>
    /// Execute image spam check using OpenAI Vision
    /// </summary>
    public async Task<SpamCheckResponse> CheckAsync(SpamCheckRequest request, CancellationToken cancellationToken = default)
    {
        // Load config from database
        var config = await _configRepository.GetGlobalConfigAsync(cancellationToken);

        // Check if this check is enabled
        if (!config.ImageSpam.Enabled)
        {
            return new SpamCheckResponse
            {
                CheckName = CheckName,
                IsSpam = false,
                Details = "Check disabled",
                Confidence = 0
            };
        }

        // Check if we're using OpenAI Vision
        if (!config.ImageSpam.UseOpenAIVision)
        {
            return new SpamCheckResponse
            {
                CheckName = CheckName,
                IsSpam = false,
                Details = "OpenAI Vision not enabled",
                Confidence = 0
            };
        }

        if (request.ImageData == null)
        {
            return new SpamCheckResponse
            {
                CheckName = CheckName,
                IsSpam = false,
                Details = "No image data provided",
                Confidence = 0
            };
        }

        // Update HTTP client timeout from config
        _httpClient.Timeout = config.ImageSpam.Timeout;

        try
        {
            if (string.IsNullOrEmpty(_openAIOptions.ApiKey))
            {
                _logger.LogWarning("OpenAI API key not configured for image spam detection");
                return new SpamCheckResponse
                {
                    CheckName = CheckName,
                    IsSpam = false,
                    Details = "OpenAI API key not configured",
                    Confidence = 0,
                    Error = new InvalidOperationException("OpenAI API key not configured")
                };
            }

            // Convert image to base64
            using var ms = new MemoryStream();
            await request.ImageData.CopyToAsync(ms, cancellationToken);
            var base64Image = Convert.ToBase64String(ms.ToArray());

            var prompt = BuildPrompt(request.Message);

            var apiRequest = new
            {
                model = _openAIOptions.Model,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "text", text = prompt },
                            new
                            {
                                type = "image_url",
                                image_url = new
                                {
                                    url = $"data:image/jpeg;base64,{base64Image}",
                                    detail = "high"
                                }
                            }
                        }
                    }
                },
                max_tokens = _openAIOptions.MaxTokens,
                temperature = 0.1
            };

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_openAIOptions.ApiKey}");

            _logger.LogDebug("ImageSpam check for user {UserId}: Calling OpenAI Vision API",
                request.UserId);

            var response = await _httpClient.PostAsJsonAsync(
                "https://api.openai.com/v1/chat/completions",
                apiRequest,
                cancellationToken);

            // Handle rate limiting
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta?.TotalSeconds ?? 60;
                _logger.LogWarning("OpenAI Vision rate limit hit. Retry after {RetryAfter} seconds", retryAfter);

                return new SpamCheckResponse
                {
                    CheckName = CheckName,
                    IsSpam = false, // Fail open during rate limits
                    Details = $"OpenAI rate limited, retry after {retryAfter}s",
                    Confidence = 0,
                    Error = new HttpRequestException($"OpenAI API rate limited")
                };
            }

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("OpenAI Vision API error: {StatusCode} - {Error}", response.StatusCode, error);

                return new SpamCheckResponse
                {
                    CheckName = CheckName,
                    IsSpam = false, // Fail open
                    Details = $"OpenAI API error: {response.StatusCode}",
                    Confidence = 0,
                    Error = new HttpRequestException($"OpenAI API error: {response.StatusCode}")
                };
            }

            var result = await response.Content.ReadFromJsonAsync<OpenAIVisionApiResponse>(cancellationToken: cancellationToken);
            var content = result?.Choices?[0]?.Message?.Content;

            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.LogWarning("Empty response from OpenAI Vision for user {UserId}", request.UserId);
                return new SpamCheckResponse
                {
                    CheckName = CheckName,
                    IsSpam = false,
                    Details = "Empty OpenAI response",
                    Confidence = 0,
                    Error = new InvalidOperationException("Empty OpenAI response")
                };
            }

            return ParseSpamResponse(content, request.UserId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Image spam check failed for user {UserId}", request.UserId);
            return new SpamCheckResponse
            {
                CheckName = CheckName,
                IsSpam = false, // Fail open
                Details = "Image spam check failed due to error",
                Confidence = 0,
                Error = ex
            };
        }
    }

    /// <summary>
    /// Build prompt for OpenAI Vision analysis
    /// </summary>
    private static string BuildPrompt(string? messageText) => $$"""
        You are a spam detection system for Telegram. Analyze this image and determine if it's spam/scam.
        {{(messageText != null ? $"Message text: \"{messageText}\"" : "No message text provided.")}}

        Common Telegram spam patterns:
        1. Crypto airdrop scams (fake celebrity accounts, "send X get Y back", urgent claims)
        2. Phishing - fake wallet UIs, transaction confirmations, login screens
        3. Airdrop scams with urgent language ("claim now", "limited time") + ticker symbols ($TOKEN, $COIN)
        4. Impersonation - fake verified accounts, customer support
        5. Get-rich-quick schemes ("earn daily", "passive income", guaranteed profits)
        6. Screenshots of fake transactions or social media posts
        7. Referral spam with voucher codes or suspicious links

        Respond ONLY with valid JSON (no markdown, no code blocks):
        {
          "spam": true or false,
          "confidence": 1-100,
          "reason": "specific explanation",
          "patterns_detected": ["list", "of", "patterns"]
        }
        """;

    /// <summary>
    /// Parse OpenAI Vision response and create spam check result
    /// </summary>
    private SpamCheckResponse ParseSpamResponse(string content, string userId)
    {
        try
        {
            // Remove markdown code blocks if present
            content = content.Trim();
            if (content.StartsWith("```"))
            {
                var lines = content.Split('\n');
                content = string.Join('\n', lines.Skip(1).SkipLast(1));
            }

            var response = JsonSerializer.Deserialize<OpenAIVisionResponse>(
                content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (response == null)
            {
                _logger.LogWarning("Failed to deserialize OpenAI Vision response for user {UserId}: {Content}",
                    userId, content);
                return new SpamCheckResponse
                {
                    CheckName = CheckName,
                    IsSpam = false,
                    Details = "Failed to parse OpenAI response",
                    Confidence = 0,
                    Error = new InvalidOperationException("Failed to parse OpenAI response")
                };
            }

            _logger.LogDebug("OpenAI Vision analysis for user {UserId}: Spam={Spam}, Confidence={Confidence}, Reason={Reason}",
                userId, response.Spam, response.Confidence, response.Reason);

            var details = response.Reason ?? "No reason provided";
            if (response.PatternsDetected?.Length > 0)
            {
                details += $" (Patterns: {string.Join(", ", response.PatternsDetected)})";
            }

            return new SpamCheckResponse
            {
                CheckName = CheckName,
                IsSpam = response.Spam,
                Details = details,
                Confidence = response.Confidence
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing OpenAI Vision response for user {UserId}: {Content}",
                userId, content);
            return new SpamCheckResponse
            {
                CheckName = CheckName,
                IsSpam = false,
                Details = "Failed to parse spam analysis",
                Confidence = 0,
                Error = ex
            };
        }
    }
}

/// <summary>
/// OpenAI API response structure for Vision
/// </summary>
internal record OpenAIVisionApiResponse(
    [property: JsonPropertyName("choices")] VisionChoice[]? Choices
);

/// <summary>
/// OpenAI choice structure for Vision
/// </summary>
internal record VisionChoice(
    [property: JsonPropertyName("message")] VisionMessage? Message
);

/// <summary>
/// OpenAI message structure for Vision
/// </summary>
internal record VisionMessage(
    [property: JsonPropertyName("content")] string? Content
);

/// <summary>
/// OpenAI Vision response structure
/// </summary>
internal record OpenAIVisionResponse(
    [property: JsonPropertyName("spam")] bool Spam,
    [property: JsonPropertyName("confidence")] int Confidence,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("patterns_detected")] string[]? PatternsDetected
);