using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Abstractions;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Helpers;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.Services;

namespace TelegramGroupsAdmin.ContentDetection.Checks;

/// <summary>
/// Spam check that analyzes images using 3-layer detection strategy (ML-5)
/// Layer 1: Hash similarity (fastest, cheapest, most reliable for known spam)
/// Layer 2: OCR + text spam checks (fast, cheap, good for text-heavy images)
/// Layer 3: OpenAI Vision fallback (slow, expensive, comprehensive)
/// </summary>
public class ImageSpamCheck(
    ILogger<ImageSpamCheck> logger,
    IHttpClientFactory httpClientFactory,
    IImageTextExtractionService imageTextExtractionService,
    IServiceProvider serviceProvider,
    ISpamDetectionConfigRepository configRepository,
    IPhotoHashService photoHashService,
    IImageTrainingSamplesRepository imageTrainingSamplesRepository) : IContentCheck
{
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly IImageTextExtractionService _imageTextExtractionService = imageTextExtractionService;
    private readonly IServiceProvider _serviceProvider = serviceProvider; // Lazy resolve to break circular dependency
    private readonly ISpamDetectionConfigRepository _configRepository = configRepository;
    private readonly IPhotoHashService _photoHashService = photoHashService;
    private readonly IImageTrainingSamplesRepository _imageTrainingSamplesRepository = imageTrainingSamplesRepository;

    public CheckName CheckName => CheckName.ImageSpam;

    /// <summary>
    /// Check if image spam check should be executed
    /// </summary>
    public bool ShouldExecute(ContentCheckRequest request)
    {
        // Run if any image source is provided
        return request.ImageData != null ||
               !string.IsNullOrEmpty(request.PhotoFileId) ||
               !string.IsNullOrEmpty(request.PhotoLocalPath);
    }

    /// <summary>
    /// Execute image spam check using OpenAI Vision
    /// </summary>
    public async ValueTask<ContentCheckResponse> CheckAsync(ContentCheckRequestBase request)
    {
        var req = (ImageCheckRequest)request;

        try
        {
            // Load config
            var config = await _configRepository.GetEffectiveConfigAsync(req.ChatId, req.CancellationToken);
            var imageConfig = config.ImageSpam;

            // ML-5 Layer 1: Hash similarity check (fastest - check if we've seen this spam before)
            if (imageConfig.UseHashSimilarity &&
                !string.IsNullOrEmpty(req.PhotoLocalPath) &&
                File.Exists(req.PhotoLocalPath))
            {
                var photoHash = await _photoHashService.ComputePhotoHashAsync(req.PhotoLocalPath);
                if (photoHash != null)
                {
                    // Query training samples (limited by config for performance)
                    var trainingSamples = await _imageTrainingSamplesRepository.GetRecentSamplesAsync(
                        imageConfig.MaxTrainingSamplesToCompare,
                        req.CancellationToken);

                    if (trainingSamples.Count > 0)
                    {
                        // Find best match by comparing hash similarity
                        double bestSimilarity = 0.0;
                        bool? matchedSpamLabel = null;

                        foreach (var (sampleHash, isSpam) in trainingSamples)
                        {
                            var similarity = _photoHashService.CompareHashes(photoHash, sampleHash);
                            if (similarity > bestSimilarity)
                            {
                                bestSimilarity = similarity;
                                matchedSpamLabel = isSpam;
                            }
                        }

                        // Check if similarity meets threshold
                        if (bestSimilarity >= imageConfig.HashSimilarityThreshold)
                        {
                            logger.LogInformation(
                                "ImageSpam Layer 1: Hash match found ({Similarity:F2}% >= {Threshold:F2}%). Returning early with {Classification}",
                                bestSimilarity * 100, imageConfig.HashSimilarityThreshold * 100,
                                matchedSpamLabel == true ? "SPAM" : "CLEAN");

                            return new ContentCheckResponse
                            {
                                CheckName = CheckName,
                                Result = matchedSpamLabel == true ? CheckResultType.Spam : CheckResultType.Clean,
                                Details = $"Image hash {bestSimilarity:P0} similar to known {(matchedSpamLabel == true ? "spam" : "ham")} sample",
                                Confidence = imageConfig.HashMatchConfidence
                            };
                        }

                        logger.LogDebug(
                            "ImageSpam Layer 1: Best hash similarity {Similarity:F2}% below threshold {Threshold:F2}%, proceeding to OCR",
                            bestSimilarity * 100, imageConfig.HashSimilarityThreshold * 100);
                    }
                    else
                    {
                        logger.LogDebug("ImageSpam Layer 1: No training samples available for hash comparison");
                    }
                }
                else
                {
                    logger.LogWarning("Failed to compute photo hash for {PhotoPath}", req.PhotoLocalPath);
                }
            }

            // ML-5 Layer 2: OCR + text-based spam detection
            if (imageConfig.UseOCR &&
                !string.IsNullOrEmpty(req.PhotoLocalPath) &&
                File.Exists(req.PhotoLocalPath))
            {
                var extractedText = await _imageTextExtractionService.ExtractTextAsync(
                    req.PhotoLocalPath,
                    req.CancellationToken);

                if (!string.IsNullOrWhiteSpace(extractedText) &&
                    extractedText.Length >= imageConfig.MinOcrTextLength)
                {
                    logger.LogDebug(
                        "ImageSpam Layer 2: OCR extracted {CharCount} characters from {PhotoPath}, running text-based spam checks",
                        extractedText.Length, Path.GetFileName(req.PhotoLocalPath));

                    // Create text-only request (no ImageData = ImageSpamCheck won't re-execute)
                    var ocrRequest = new ContentCheckRequest
                    {
                        Message = extractedText,
                        UserId = req.UserId,
                        UserName = req.UserName,
                        ChatId = req.ChatId,
                        // Use defaults for properties not on ImageCheckRequest
                        Metadata = new ContentCheckMetadata(),
                        CheckOnly = false,
                        HasSpamFlags = false,
                        IsUserTrusted = false,
                        IsUserAdmin = false,
                        ImageData = null,  // ← Key: No image = ImageSpamCheck won't run
                        PhotoFileId = null,
                        PhotoUrl = null,
                        PhotoLocalPath = null,
                        Urls = []
                    };

                    // Run all text-based spam checks on OCR text (includes OpenAI veto if needed)
                    // Lazy-resolve engine to break circular dependency
                    var contentDetectionEngine = _serviceProvider.GetRequiredService<IContentDetectionEngine>();
                    var ocrResult = await contentDetectionEngine.CheckMessageAsync(ocrRequest, req.CancellationToken);

                    // Check if result is confident enough to skip expensive Vision API
                    if (ocrResult.MaxConfidence >= imageConfig.OcrConfidenceThreshold)
                    {
                        var flaggedChecks = ocrResult.CheckResults
                            .Where(c => c.Result == CheckResultType.Spam)
                            .Select(c => c.CheckName)
                            .ToList();

                        logger.LogInformation(
                            "ImageSpam Layer 2: OCR text checks confident ({Confidence}% >= {Threshold}%), returning early with {Result}",
                            ocrResult.MaxConfidence, imageConfig.OcrConfidenceThreshold, ocrResult.IsSpam ? "SPAM" : "CLEAN");

                        return new ContentCheckResponse
                        {
                            CheckName = CheckName,
                            Result = ocrResult.IsSpam ? CheckResultType.Spam : CheckResultType.Clean,
                            Details = $"OCR detected text analyzed by {flaggedChecks.Count} checks: {string.Join(", ", flaggedChecks)}",
                            Confidence = ocrResult.MaxConfidence
                        };
                    }

                    // OCR checks uncertain - proceed to Vision (Layer 3)
                    logger.LogDebug(
                        "ImageSpam Layer 2: OCR text checks uncertain (confidence {Confidence}% < {Threshold}%), proceeding to Vision",
                        ocrResult.MaxConfidence, imageConfig.OcrConfidenceThreshold);
                }
                else if (!string.IsNullOrWhiteSpace(extractedText))
                {
                    logger.LogDebug("ImageSpam Layer 2: OCR extracted only {CharCount} characters (< {MinLength}), too short for text analysis",
                        extractedText.Length, imageConfig.MinOcrTextLength);
                }
            }

            // ML-5 Layer 3: OpenAI Vision fallback (existing logic)
            if (string.IsNullOrEmpty(req.ApiKey))
            {
                logger.LogWarning("OpenAI API key not configured for image spam detection");
                return new ContentCheckResponse
                {
                    CheckName = CheckName,
                    Result = CheckResultType.Clean,
                    Details = "OpenAI API key not configured",
                    Confidence = 0,
                    Error = new InvalidOperationException("OpenAI API key not configured")
                };
            }

            // Build image URL - either use provided PhotoUrl, construct from PhotoFileId, or convert PhotoLocalPath
            string imageUrl;
            if (!string.IsNullOrEmpty(req.PhotoUrl))
            {
                // Use direct URL if provided
                imageUrl = req.PhotoUrl;
            }
            else if (!string.IsNullOrEmpty(req.PhotoLocalPath) && File.Exists(req.PhotoLocalPath))
            {
                // Convert local file to base64 data URI for OpenAI Vision API
                var imageBytes = await File.ReadAllBytesAsync(req.PhotoLocalPath, req.CancellationToken);
                var base64 = Convert.ToBase64String(imageBytes);
                var mimeType = req.PhotoLocalPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
                imageUrl = $"data:{mimeType};base64,{base64}";
            }
            else if (!string.IsNullOrEmpty(req.PhotoFileId))
            {
                // For Telegram file IDs, we'd need to download the image first
                // This should be handled by the caller, so log a warning
                logger.LogWarning("PhotoFileId provided but no PhotoUrl - image cannot be processed");
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

            logger.LogDebug("ImageSpam check for user {UserId}: Calling OpenAI Vision API",
                req.UserId);

            // Use named "OpenAI" HttpClient (configured in ServiceCollectionExtensions)
            var httpClient = _httpClientFactory.CreateClient("OpenAI");
            var response = await httpClient.PostAsJsonAsync(
                "chat/completions",
                apiRequest,
                req.CancellationToken);

            // Handle rate limiting
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta?.TotalSeconds ?? 60;
                logger.LogWarning("OpenAI Vision rate limit hit. Retry after {RetryAfter} seconds", retryAfter);

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
                logger.LogError("OpenAI Vision API error: {StatusCode} - {Error}", response.StatusCode, error);

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
                logger.LogWarning("Empty response from OpenAI Vision for user {UserId}", req.UserId);
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
            return ContentCheckHelpers.CreateFailureResponse(CheckName, ex, logger, req.UserId);
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

        return $$"""
            {{systemPrompt}}

            {{messageContext}}

            Respond ONLY with valid JSON (no markdown, no code blocks):
            {
              "spam": true or false,
              "confidence": 1-100,
              "reason": "specific explanation",
              "patterns_detected": ["list", "of", "patterns"]
            }
            """;
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
                logger.LogWarning("Failed to deserialize OpenAI Vision response for user {UserId}: {Content}",
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

            logger.LogDebug("OpenAI Vision analysis for user {UserId}: Spam={Spam}, Confidence={Confidence}, Reason={Reason}",
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
            logger.LogError(ex, "Error parsing OpenAI Vision response for user {UserId}: {Content}",
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