using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TelegramGroupsAdmin.Configuration;

namespace TelegramGroupsAdmin.Services.Vision;

public class OpenAIVisionSpamDetectionService : IVisionSpamDetectionService
{
    private readonly HttpClient _httpClient;
    private readonly OpenAIOptions _options;
    private readonly ILogger<OpenAIVisionSpamDetectionService> _logger;

    public OpenAIVisionSpamDetectionService(
        HttpClient httpClient,
        IOptions<OpenAIOptions> options,
        ILogger<OpenAIVisionSpamDetectionService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<CheckResult> AnalyzeImageAsync(
        Stream imageStream,
        string? messageText,
        CancellationToken ct = default)
    {
        try
        {
            // Convert image to base64
            using var ms = new MemoryStream();
            await imageStream.CopyToAsync(ms, ct);
            var base64Image = Convert.ToBase64String(ms.ToArray());

            var prompt = BuildPrompt(messageText);

            var request = new
            {
                model = _options.Model,
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
                max_tokens = _options.MaxTokens,
                temperature = 0.1
            };

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_options.ApiKey}");

            var response = await _httpClient.PostAsJsonAsync(
                "https://api.openai.com/v1/chat/completions",
                request,
                ct);

            // Handle 429 Too Many Requests (rate limiting)
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta?.TotalSeconds ?? 60;
                _logger.LogWarning(
                    "OpenAI rate limit hit. Retry after {RetryAfter} seconds",
                    retryAfter);

                // Return non-spam to fail open (don't block legitimate messages due to rate limits)
                return new CheckResult(false, $"Rate limit exceeded, retry after {retryAfter}s", 0);
            }

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError(
                    "OpenAI API error: {StatusCode} - {Error}",
                    response.StatusCode,
                    error);
                return new CheckResult(false, "Image analysis unavailable", 0);
            }

            var result = await response.Content.ReadFromJsonAsync<OpenAIResponse>(cancellationToken: ct);
            var content = result?.Choices?[0]?.Message?.Content;

            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.LogWarning("Empty response from OpenAI Vision");
                return new CheckResult(false, "Image analysis failed", 0);
            }

            return ParseSpamResponse(content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling OpenAI Vision API");
            return new CheckResult(false, "Image analysis error", 0);
        }
    }

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

    private CheckResult ParseSpamResponse(string content)
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
                _logger.LogWarning("Failed to deserialize OpenAI response: {Content}", content);
                return new CheckResult(false, "Failed to parse response", 0);
            }

            _logger.LogInformation(
                "OpenAI Vision analysis: Spam={Spam}, Confidence={Confidence}, Reason={Reason}",
                response.Spam,
                response.Confidence,
                response.Reason);

            return new CheckResult(
                response.Spam,
                response.Reason ?? "No reason provided",
                response.Confidence);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing OpenAI response: {Content}", content);
            return new CheckResult(false, "Failed to parse spam analysis", 0);
        }
    }

    private record OpenAIResponse(
        [property: JsonPropertyName("choices")] Choice[]? Choices
    );

    private record Choice(
        [property: JsonPropertyName("message")] Message? Message
    );

    private record Message(
        [property: JsonPropertyName("content")] string? Content
    );

    private record OpenAIVisionResponse(
        [property: JsonPropertyName("spam")] bool Spam,
        [property: JsonPropertyName("confidence")] int Confidence,
        [property: JsonPropertyName("reason")] string? Reason,
        [property: JsonPropertyName("patterns_detected")] string[]? PatternsDetected
    );
}
