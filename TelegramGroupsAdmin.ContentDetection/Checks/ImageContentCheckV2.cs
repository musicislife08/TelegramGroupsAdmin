using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.ContentDetection.Abstractions;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Helpers;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Configuration.Models.ContentDetection;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Core.Services.AI;

namespace TelegramGroupsAdmin.ContentDetection.Checks;

/// <summary>
/// V2 image spam check with 3-layer detection strategy and proper scoring.
/// Provider-agnostic - uses IChatService for multi-provider Vision support.
/// Layer 1: Hash similarity (fastest, cheapest, most reliable for known spam)
/// Layer 2: OCR + text spam checks (fast, cheap, good for text-heavy images)
/// Layer 3: AI Vision fallback (slow, expensive, comprehensive)
/// Scoring: Maps confidence (0-100%) to points (0.0-5.0)
/// </summary>
public class ImageContentCheckV2(
    ILogger<ImageContentCheckV2> logger,
    IChatService chatService,
    IImageTextExtractionService imageTextExtractionService,
    IServiceProvider serviceProvider,
    IContentDetectionConfigRepository configRepository,
    IPhotoHashService photoHashService,
    IImageTrainingSamplesRepository imageTrainingSamplesRepository) : IContentCheckV2
{
    private readonly IImageTextExtractionService _imageTextExtractionService = imageTextExtractionService;
    private readonly IServiceProvider _serviceProvider = serviceProvider; // Lazy resolve to break circular dependency
    private readonly IContentDetectionConfigRepository _configRepository = configRepository;
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
    /// Execute image spam check using 3-layer strategy
    /// Returns score 0.0-5.0 based on confidence
    /// </summary>
    public async ValueTask<ContentCheckResponseV2> CheckAsync(ContentCheckRequestBase request)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        var req = (ImageCheckRequest)request;

        try
        {
            // Load config
            var config = await _configRepository.GetEffectiveConfigAsync(req.ChatId, req.CancellationToken);
            var imageConfig = config.ImageSpam;

            // Extract OCR text early so it's available for all return paths (for AI veto passthrough)
            // Trade-off: OCR runs even if hash similarity (Layer 1) returns early, but this ensures
            // AI veto can analyze image text for false positive detection. OCR is CPU-bound (Tesseract)
            // but typically completes in <100ms for typical image sizes.
            string? extractedOcrText = null;
            if (imageConfig.UseOCR &&
                !string.IsNullOrEmpty(req.PhotoLocalPath) &&
                File.Exists(req.PhotoLocalPath))
            {
                extractedOcrText = await _imageTextExtractionService.ExtractTextAsync(
                    req.PhotoLocalPath,
                    req.CancellationToken);
            }

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
                            // Map confidence to score
                            var score = (imageConfig.HashMatchConfidence / 100.0) * AIConstants.ConfidenceToScoreMultiplier;

                            // If matched HAM (not spam), abstain (don't give negative signal in V2)
                            if (matchedSpamLabel == false)
                            {
                                logger.LogInformation(
                                    "ImageSpam V2 Layer 1: Hash match found ({Similarity:F2}% >= {Threshold:F2}%) but matched HAM sample, abstaining",
                                    bestSimilarity * 100, imageConfig.HashSimilarityThreshold * 100);

                                return new ContentCheckResponseV2
                                {
                                    CheckName = CheckName,
                                    Score = 0.0,
                                    Abstained = true,
                                    Details = $"Image hash {bestSimilarity:P0} similar to known ham sample (abstaining)",
                                    ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds,
                                    OcrExtractedText = extractedOcrText
                                };
                            }

                            logger.LogInformation(
                                "ImageSpam V2 Layer 1: Hash match found ({Similarity:F2}% >= {Threshold:F2}%). Returning {Score:F2} points",
                                bestSimilarity * 100, imageConfig.HashSimilarityThreshold * 100, score);

                            return new ContentCheckResponseV2
                            {
                                CheckName = CheckName,
                                Score = score,
                                Abstained = false,
                                Details = $"Image hash {bestSimilarity:P0} similar to known spam sample",
                                ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds,
                                OcrExtractedText = extractedOcrText
                            };
                        }

                        logger.LogDebug(
                            "ImageSpam V2 Layer 1: Best hash similarity {Similarity:F2}% below threshold {Threshold:F2}%, proceeding to OCR",
                            bestSimilarity * 100, imageConfig.HashSimilarityThreshold * 100);
                    }
                    else
                    {
                        logger.LogDebug("ImageSpam V2 Layer 1: No training samples available for hash comparison");
                    }
                }
                else
                {
                    logger.LogWarning("Failed to compute photo hash for {PhotoPath}", req.PhotoLocalPath);
                }
            }

            // ML-5 Layer 2: OCR + text-based spam detection (using pre-extracted text)
            if (!string.IsNullOrWhiteSpace(extractedOcrText) &&
                extractedOcrText.Length >= imageConfig.MinOcrTextLength)
            {
                logger.LogDebug(
                    "ImageSpam V2 Layer 2: OCR extracted {CharCount} characters from {PhotoPath}, running text-based spam checks",
                    extractedOcrText.Length, Path.GetFileName(req.PhotoLocalPath));

                // Create text-only request (no ImageData = ImageSpamCheck won't re-execute)
                var ocrRequest = new ContentCheckRequest
                {
                    Message = extractedOcrText,
                    UserId = req.UserId,
                    UserName = req.UserName,
                    ChatId = req.ChatId,
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

                // Run all text-based spam checks on OCR text
                // Lazy-resolve engine to break circular dependency
                var contentDetectionEngine = _serviceProvider.GetRequiredService<IContentDetectionEngine>();
                var ocrResult = await contentDetectionEngine.CheckMessageAsync(ocrRequest, req.CancellationToken);

                // Check if result is confident enough to skip expensive Vision API
                if (ocrResult.MaxConfidence >= imageConfig.OcrConfidenceThreshold)
                {
                    // Map OCR text check confidence to score
                    var score = ocrResult.IsSpam ? (ocrResult.MaxConfidence / 100.0) * AIConstants.ConfidenceToScoreMultiplier : 0.0;

                    // V2: Only return score if spam detected, otherwise abstain
                    if (!ocrResult.IsSpam)
                    {
                        logger.LogDebug(
                            "ImageSpam V2 Layer 2: OCR text checks returned clean ({Confidence}%), abstaining",
                            ocrResult.MaxConfidence);

                        return new ContentCheckResponseV2
                        {
                            CheckName = CheckName,
                            Score = 0.0,
                            Abstained = true,
                            Details = "OCR detected text analyzed as clean, abstaining",
                            ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds,
                            OcrExtractedText = extractedOcrText
                        };
                    }

                    var flaggedChecks = ocrResult.CheckResults
                        .Where(c => c.Result == CheckResultType.Spam)
                        .Select(c => c.CheckName)
                        .ToList();

                    logger.LogInformation(
                        "ImageSpam V2 Layer 2: OCR text checks confident ({Confidence}% >= {Threshold}%), returning {Score:F2} points",
                        ocrResult.MaxConfidence, imageConfig.OcrConfidenceThreshold, score);

                    return new ContentCheckResponseV2
                    {
                        CheckName = CheckName,
                        Score = score,
                        Abstained = false,
                        Details = $"OCR detected spam text analyzed by {flaggedChecks.Count} checks: {string.Join(", ", flaggedChecks)}",
                        ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds,
                        OcrExtractedText = extractedOcrText
                    };
                }

                // OCR checks uncertain - proceed to Vision (Layer 3)
                logger.LogDebug(
                    "ImageSpam V2 Layer 2: OCR text checks uncertain (confidence {Confidence}% < {Threshold}%), proceeding to Vision",
                    ocrResult.MaxConfidence, imageConfig.OcrConfidenceThreshold);
            }
            else if (!string.IsNullOrWhiteSpace(extractedOcrText))
            {
                logger.LogDebug("ImageSpam V2 Layer 2: OCR extracted only {CharCount} characters (< {MinLength}), too short for text analysis",
                    extractedOcrText.Length, imageConfig.MinOcrTextLength);
            }

            // ML-5 Layer 3: AI Vision fallback (provider-agnostic via IChatService)
            return await CheckWithVisionAsync(req, startTimestamp, extractedOcrText);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in ImageSpamCheckV2 for user {UserId}, abstaining", req.UserId);
            return new ContentCheckResponseV2
            {
                CheckName = CheckName,
                Score = 0.0,
                Abstained = true,
                Details = $"Error: {ex.Message}",
                Error = ex,
                ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds,
                OcrExtractedText = null // OCR extraction may have failed
            };
        }
    }

    /// <summary>
    /// ML-5 Layer 3: AI Vision check using IChatService (supports multiple providers)
    /// </summary>
    private async Task<ContentCheckResponseV2> CheckWithVisionAsync(ImageCheckRequest req, long startTimestamp, string? extractedOcrText)
    {
        // Check if image analysis feature is available
        if (!await chatService.IsFeatureAvailableAsync(AIFeatureType.ImageAnalysis, req.CancellationToken))
        {
            logger.LogDebug("ImageSpam V2 Layer 3: Vision not configured - no AI provider for image analysis");
            return new ContentCheckResponseV2
            {
                CheckName = CheckName,
                Score = 0.0,
                Abstained = true,
                Details = "Vision not available - no AI provider configured for image analysis",
                ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds,
                OcrExtractedText = extractedOcrText
            };
        }

        // Prepare image data - either from local path or URL
        byte[]? imageData = null;
        string mimeType = "image/jpeg";

        if (!string.IsNullOrEmpty(req.PhotoLocalPath) && File.Exists(req.PhotoLocalPath))
        {
            imageData = await File.ReadAllBytesAsync(req.PhotoLocalPath, req.CancellationToken);
            mimeType = req.PhotoLocalPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
        }
        else if (!string.IsNullOrEmpty(req.PhotoFileId))
        {
            logger.LogWarning("PhotoFileId provided but no PhotoLocalPath - image cannot be processed, abstaining");
            return new ContentCheckResponseV2
            {
                CheckName = CheckName,
                Score = 0.0,
                Abstained = true,
                Details = "No local image file available for Vision analysis",
                ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds,
                OcrExtractedText = extractedOcrText
            };
        }
        else
        {
            return new ContentCheckResponseV2
            {
                CheckName = CheckName,
                Score = 0.0,
                Abstained = true,
                Details = "No image data provided",
                ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds,
                OcrExtractedText = extractedOcrText
            };
        }

        var prompt = BuildPrompt(req.Message, req.CustomPrompt);

        logger.LogDebug("ImageSpam V2 check for user {UserId}: Calling AI Vision API", req.UserId);

        try
        {
            // Temperature uses feature config default (set in AI Integration settings)
            var result = await chatService.GetVisionCompletionAsync(
                AIFeatureType.ImageAnalysis,
                GetDefaultImagePrompt(),
                prompt,
                imageData,
                mimeType,
                new ChatCompletionOptions
                {
                    MaxTokens = AIConstants.ImageVisionMaxTokens
                },
                req.CancellationToken);

            if (result == null || string.IsNullOrWhiteSpace(result.Content))
            {
                logger.LogWarning("Empty response from AI Vision for user {UserId}, abstaining", req.UserId);
                return new ContentCheckResponseV2
                {
                    CheckName = CheckName,
                    Score = 0.0,
                    Abstained = true,
                    Details = "Empty AI Vision response",
                    ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds,
                    OcrExtractedText = extractedOcrText
                };
            }

            return ParseSpamResponse(result.Content, req.UserId, startTimestamp, extractedOcrText);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AI Vision API error for user {UserId}, abstaining", req.UserId);
            return new ContentCheckResponseV2
            {
                CheckName = CheckName,
                Score = 0.0,
                Abstained = true,
                Details = $"AI Vision error: {ex.Message}",
                Error = ex,
                ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds,
                OcrExtractedText = extractedOcrText
            };
        }
    }

    /// <summary>
    /// Build prompt for AI Vision analysis
    /// Uses configurable system prompt if provided, otherwise uses default
    /// </summary>
    private static string BuildPrompt(string? messageText, string? customPrompt)
    {
        var messageContext = messageText != null
            ? $"Message text: \"{messageText}\""
            : "No message text provided.";

        return $$"""
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
    /// Get default image spam detection prompt (used as system prompt for Vision)
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
    /// Parse AI Vision response and create V2 spam check result with scoring
    /// </summary>
    private ContentCheckResponseV2 ParseSpamResponse(string content, long userId, long startTimestamp, string? extractedOcrText)
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

            var response = JsonSerializer.Deserialize<VisionSpamResponse>(
                content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (response == null)
            {
                logger.LogWarning("Failed to deserialize AI Vision response for user {UserId}: {Content}, abstaining",
                    userId, content);
                return new ContentCheckResponseV2
                {
                    CheckName = CheckName,
                    Score = 0.0,
                    Abstained = true,
                    Details = "Failed to parse AI Vision response",
                    ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds,
                    OcrExtractedText = extractedOcrText
                };
            }

            logger.LogDebug("AI Vision V2 analysis for user {UserId}: Spam={Spam}, Confidence={Confidence}, Reason={Reason}",
                userId, response.Spam, response.Confidence, response.Reason);

            var details = response.Reason ?? "No reason provided";
            if (response.PatternsDetected?.Length > 0)
            {
                details += $" (Patterns: {string.Join(", ", response.PatternsDetected)})";
            }

            // V2: Only return score if spam detected, otherwise abstain
            if (!response.Spam)
            {
                return new ContentCheckResponseV2
                {
                    CheckName = CheckName,
                    Score = 0.0,
                    Abstained = true,
                    Details = $"AI Vision: Clean ({details})",
                    ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds,
                    OcrExtractedText = extractedOcrText
                };
            }

            // Map confidence to score: 0-100% → 0.0-5.0 points
            var score = (response.Confidence / 100.0) * AIConstants.ConfidenceToScoreMultiplier;

            return new ContentCheckResponseV2
            {
                CheckName = CheckName,
                Score = score,
                Abstained = false,
                Details = details,
                ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds,
                OcrExtractedText = extractedOcrText
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error parsing AI Vision response for user {UserId}: {Content}, abstaining",
                userId, content);
            return new ContentCheckResponseV2
            {
                CheckName = CheckName,
                Score = 0.0,
                Abstained = true,
                Details = "Failed to parse spam analysis",
                Error = ex,
                ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds,
                OcrExtractedText = extractedOcrText
            };
        }
    }
}

/// <summary>
/// Expected JSON response structure from AI Vision for spam detection.
/// This is the format we request in our prompts.
/// </summary>
internal record VisionSpamResponse(
    [property: JsonPropertyName("spam")] bool Spam,
    [property: JsonPropertyName("confidence")] int Confidence,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("patterns_detected")] string[]? PatternsDetected
);
