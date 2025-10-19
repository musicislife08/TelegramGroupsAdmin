using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Abstractions;
using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Checks;

/// <summary>
/// Spam check that analyzes images using OpenAI Vision API for spam detection
/// Based on existing OpenAIVisionSpamDetectionService implementation
/// </summary>
public class ImageSpamCheck : IContentCheck
{
    private readonly ILogger<ImageSpamCheck> _logger;
    private readonly HttpClient _httpClient;

    public string CheckName => "ImageSpam";

    public ImageSpamCheck(
        ILogger<ImageSpamCheck> logger,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();

        // Configure HTTP client
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Check if image spam check should be executed
    /// </summary>
    public bool ShouldExecute(ContentCheckRequest request)
    {
        // Only run if image data is provided
        return request.ImageData != null;
    }

    /// <summary>
    /// Execute image spam check using OpenAI Vision
    /// </summary>
    public async Task<ContentCheckResponse> CheckAsync(ContentCheckRequestBase request)
    {
        var req = (ImageCheckRequest)request;

        try
        {
            if (string.IsNullOrEmpty(req.ApiKey))
            {
                _logger.LogWarning("OpenAI API key not configured for image spam detection");
                return new ContentCheckResponse
                {
                    CheckName = CheckName,
                    Result = CheckResultType.Clean,
                    Details = "OpenAI API key not configured",
                    Confidence = 0,
                    Error = new InvalidOperationException("OpenAI API key not configured")
                };
            }

            // Build image URL - either use provided PhotoUrl or construct from PhotoFileId
            string imageUrl;
            if (!string.IsNullOrEmpty(req.PhotoUrl))
            {
                // Use direct URL if provided
                imageUrl = req.PhotoUrl;
            }
            else if (!string.IsNullOrEmpty(req.PhotoFileId))
            {
                // For Telegram file IDs, we'd need to download the image first
                // This should be handled by the caller, so log a warning
                _logger.LogWarning("PhotoFileId provided but no PhotoUrl - image cannot be processed");
                return new ContentCheckResponse
                {
                    CheckName = CheckName,
                    Result = CheckResultType.Clean,
                    Details = "No image URL provided",
                    Confidence = 0
                };
            }
            else
            {
                return new ContentCheckResponse
                {
                    CheckName = CheckName,
                    Result = CheckResultType.Clean,
                    Details = "No image data provided",
                    Confidence = 0
                };
            }

            var prompt = BuildPrompt(req.Message, req.CustomPrompt);

            var apiRequest = new
            {
                model = "gpt-4o-mini",
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
                                    url = imageUrl,
                                    detail = "high"
                                }
                            }
                        }
                    }
                },
                max_tokens = 300,
                temperature = 0.1
            };

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {req.ApiKey}");

            _logger.LogDebug("ImageSpam check for user {UserId}: Calling OpenAI Vision API",
                req.UserId);

            var response = await _httpClient.PostAsJsonAsync(
                "https://api.openai.com/v1/chat/completions",
                apiRequest,
                req.CancellationToken);

            // Handle rate limiting
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta?.TotalSeconds ?? 60;
                _logger.LogWarning("OpenAI Vision rate limit hit. Retry after {RetryAfter} seconds", retryAfter);

                return new ContentCheckResponse
                {
                    CheckName = CheckName,
                    Result = CheckResultType.Clean, // Fail open during rate limits
                    Details = $"OpenAI rate limited, retry after {retryAfter}s",
                    Confidence = 0,
                    Error = new HttpRequestException($"OpenAI API rate limited")
                };
            }

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(req.CancellationToken);
                _logger.LogError("OpenAI Vision API error: {StatusCode} - {Error}", response.StatusCode, error);

                return new ContentCheckResponse
                {
                    CheckName = CheckName,
                    Result = CheckResultType.Clean, // Fail open
                    Details = $"OpenAI API error: {response.StatusCode}",
                    Confidence = 0,
                    Error = new HttpRequestException($"OpenAI API error: {response.StatusCode}")
                };
            }

            var result = await response.Content.ReadFromJsonAsync<OpenAIVisionApiResponse>(cancellationToken: req.CancellationToken);
            var content = result?.Choices?[0]?.Message?.Content;

            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.LogWarning("Empty response from OpenAI Vision for user {UserId}", req.UserId);
                return new ContentCheckResponse
                {
                    CheckName = CheckName,
                    Result = CheckResultType.Clean,
                    Details = "Empty OpenAI response",
                    Confidence = 0,
                    Error = new InvalidOperationException("Empty OpenAI response")
                };
            }

            return ParseSpamResponse(content, req.UserId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Image spam check failed for user {UserId}", req.UserId);
            return new ContentCheckResponse
            {
                CheckName = CheckName,
                Result = CheckResultType.Clean, // Fail open
                Details = "Image spam check failed due to error",
                Confidence = 0,
                Error = ex
            };
        }
    }

    /// <summary>
    /// Build prompt for OpenAI Vision analysis
    /// Uses configurable system prompt if provided, otherwise uses default
    /// </summary>
    private static string BuildPrompt(string? messageText, string? customPrompt)
    {
        // Use custom prompt if provided, otherwise use default
        var systemPrompt = customPrompt ?? GetDefaultImagePrompt();

        var messageContext = messageText != null
            ? $"Message text: \"{messageText}\""
            : "No message text provided.";

        return $"{systemPrompt}\n\n{messageContext}\n\nRespond ONLY with valid JSON (no markdown, no code blocks):\n{{\n  \"spam\": true or false,\n  \"confidence\": 1-100,\n  \"reason\": \"specific explanation\",\n  \"patterns_detected\": [\"list\", \"of\", \"patterns\"]\n}}";
    }

    /// <summary>
    /// Get default image spam detection prompt (used when SystemPrompt is not configured)
    /// </summary>
    private static string GetDefaultImagePrompt() => """
        You are a spam detection system for Telegram. Analyze this image and determine if it's spam/scam.

        Common Telegram spam patterns:
        1. Crypto airdrop scams (fake celebrity accounts, "send X get Y back", urgent claims)
        2. Phishing - fake wallet UIs, transaction confirmations, login screens
        3. Airdrop scams with urgent language ("claim now", "limited time") + ticker symbols ($TOKEN, $COIN)
        4. Impersonation - fake verified accounts, customer support
        5. Get-rich-quick schemes ("earn daily", "passive income", guaranteed profits)
        6. Screenshots of fake transactions or social media posts
        7. Referral spam with voucher codes or suspicious links

        IMPORTANT: Legitimate crypto trading news (market analysis, price movements, trading volume) is NOT spam.
        Only flag content that is clearly attempting to scam users or steal money.
        """;

    /// <summary>
    /// Parse OpenAI Vision response and create spam check result
    /// </summary>
    private ContentCheckResponse ParseSpamResponse(string content, long userId)
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
                return new ContentCheckResponse
                {
                    CheckName = CheckName,
                    Result = CheckResultType.Clean,
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

            var result = response.Spam ? CheckResultType.Spam : CheckResultType.Clean;

            return new ContentCheckResponse
            {
                CheckName = CheckName,
                Result = result,
                Details = details,
                Confidence = response.Confidence
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing OpenAI Vision response for user {UserId}: {Content}",
                userId, content);
            return new ContentCheckResponse
            {
                CheckName = CheckName,
                Result = CheckResultType.Clean,
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