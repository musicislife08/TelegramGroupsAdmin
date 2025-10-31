using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Abstractions;
using TelegramGroupsAdmin.ContentDetection.Configuration;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Helpers;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.Services;

namespace TelegramGroupsAdmin.ContentDetection.Checks;

/// <summary>
/// Spam check that analyzes videos using 3-layer detection strategy (ML-6)
/// Layer 1: Keyframe hash similarity (fastest, cheapest, most reliable for known spam)
/// Layer 2: OCR on frames + text spam checks (fast, cheap, good for text-heavy videos)
/// Layer 3: OpenAI Vision on frames fallback (slow, expensive, comprehensive)
/// </summary>
public class VideoSpamCheck(
    ILogger<VideoSpamCheck> logger,
    IHttpClientFactory httpClientFactory,
    IVideoFrameExtractionService frameExtractionService,
    IImageTextExtractionService imageTextExtractionService,
    IServiceProvider serviceProvider,
    ISpamDetectionConfigRepository configRepository,
    IPhotoHashService photoHashService,
    IVideoTrainingSamplesRepository videoTrainingSamplesRepository) : IContentCheck
{
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly IVideoFrameExtractionService _frameExtractionService = frameExtractionService;
    private readonly IImageTextExtractionService _imageTextExtractionService = imageTextExtractionService;
    private readonly IServiceProvider _serviceProvider = serviceProvider; // Lazy resolve to break circular dependency
    private readonly ISpamDetectionConfigRepository _configRepository = configRepository;
    private readonly IPhotoHashService _photoHashService = photoHashService;
    private readonly IVideoTrainingSamplesRepository _videoTrainingSamplesRepository = videoTrainingSamplesRepository;

    public CheckName CheckName => CheckName.VideoSpam;

    /// <summary>
    /// Check if video spam check should be executed
    /// </summary>
    public bool ShouldExecute(ContentCheckRequest request)
    {
        // Run if video local path is provided
        var shouldRun = !string.IsNullOrEmpty(request.VideoLocalPath);
        logger.LogDebug("VideoSpamCheck.ShouldExecute: VideoLocalPath={VideoPath}, ShouldRun={ShouldRun}",
            request.VideoLocalPath ?? "(null)", shouldRun);
        return shouldRun;
    }

    /// <summary>
    /// Execute video spam check using 3-layer detection
    /// </summary>
    public async Task<ContentCheckResponse> CheckAsync(ContentCheckRequestBase request)
    {
        var req = (VideoCheckRequest)request;

        try
        {
            // Verify FFmpeg is available
            if (!_frameExtractionService.IsAvailable)
            {
                logger.LogWarning("FFmpeg not available, cannot analyze video");
                return new ContentCheckResponse
                {
                    CheckName = CheckName,
                    Result = CheckResultType.Clean,
                    Details = "FFmpeg not available for video analysis",
                    Confidence = 0,
                    Error = new InvalidOperationException("FFmpeg not available")
                };
            }

            // Verify video file exists
            if (!File.Exists(req.VideoLocalPath))
            {
                logger.LogWarning("Video file not found at {VideoPath}", req.VideoLocalPath);
                return new ContentCheckResponse
                {
                    CheckName = CheckName,
                    Result = CheckResultType.Clean,
                    Details = "Video file not found",
                    Confidence = 0,
                    Error = new FileNotFoundException("Video file not found", req.VideoLocalPath)
                };
            }

            // Load config
            var config = await _configRepository.GetEffectiveConfigAsync(req.ChatId, req.CancellationToken);
            var videoConfig = config.VideoSpam;

            // Extract keyframes from video
            var frames = await _frameExtractionService.ExtractKeyframesAsync(req.VideoLocalPath, req.CancellationToken);
            if (frames.Count == 0)
            {
                logger.LogWarning("Failed to extract keyframes from video at {VideoPath}", req.VideoLocalPath);
                return new ContentCheckResponse
                {
                    CheckName = CheckName,
                    Result = CheckResultType.Clean,
                    Details = "Failed to extract video frames",
                    Confidence = 0,
                    Error = new InvalidOperationException("Failed to extract video frames")
                };
            }

            logger.LogDebug("Extracted {FrameCount} keyframes from video at {VideoPath}",
                frames.Count, Path.GetFileName(req.VideoLocalPath));

            // ML-6 Layer 1: Keyframe hash similarity check (fastest - check if we've seen this spam before)
            if (videoConfig.UseHashSimilarity)
            {
                var layer1Result = await CheckKeyframeHashSimilarityAsync(frames, videoConfig, req.CancellationToken);
                if (layer1Result != null)
                {
                    // Clean up extracted frames
                    CleanupFrames(frames);
                    return layer1Result;
                }
            }

            // ML-6 Layer 2: OCR on frames + text-based spam detection
            if (videoConfig.UseOCR && _imageTextExtractionService.IsAvailable)
            {
                var layer2Result = await CheckFrameOCRAsync(frames, videoConfig, req, req.CancellationToken);
                if (layer2Result != null)
                {
                    // Clean up extracted frames
                    CleanupFrames(frames);
                    return layer2Result;
                }
            }

            // ML-6 Layer 3: OpenAI Vision fallback on representative frame
            if (videoConfig.UseOpenAIVision)
            {
                var layer3Result = await CheckFrameWithVisionAsync(frames, req, req.CancellationToken);

                // Clean up extracted frames
                CleanupFrames(frames);

                return layer3Result;
            }

            // No layers enabled or all failed - clean up and return clean
            CleanupFrames(frames);
            return new ContentCheckResponse
            {
                CheckName = CheckName,
                Result = CheckResultType.Clean,
                Details = "Video spam detection disabled or all layers failed",
                Confidence = 0
            };
        }
        catch (Exception ex)
        {
            return ContentCheckHelpers.CreateFailureResponse(CheckName, ex, logger, req.UserId);
        }
    }

    /// <summary>
    /// ML-6 Layer 1: Check keyframe hash similarity against training samples
    /// </summary>
    private async Task<ContentCheckResponse?> CheckKeyframeHashSimilarityAsync(
        List<ExtractedFrame> frames,
        VideoSpamConfig config,
        CancellationToken cancellationToken)
    {
        try
        {
            // Compute hashes for all extracted frames
            var frameHashes = new List<byte[]>();
            foreach (var frame in frames)
            {
                var hash = await _photoHashService.ComputePhotoHashAsync(frame.FramePath);
                if (hash != null)
                {
                    frameHashes.Add(hash);
                }
            }

            if (frameHashes.Count == 0)
            {
                logger.LogWarning("Failed to compute hashes for any video frames");
                return null;
            }

            // Query training samples (limited by config for performance)
            var trainingSamples = await _videoTrainingSamplesRepository.GetRecentSamplesAsync(
                config.MaxTrainingSamplesToCompare,
                cancellationToken);

            if (trainingSamples.Count == 0)
            {
                logger.LogDebug("VideoSpam Layer 1: No training samples available for hash comparison");
                return null;
            }

            // Find best match by comparing frame hashes to training sample keyframes
            double bestSimilarity = 0.0;
            bool? matchedSpamLabel = null;

            foreach (var (keyframeHashesJson, isSpam) in trainingSamples)
            {
                // Parse JSON keyframe hashes
                var keyframeHashes = JsonSerializer.Deserialize<List<KeyframeHashJson>>(keyframeHashesJson);
                if (keyframeHashes == null || keyframeHashes.Count == 0)
                {
                    continue;
                }

                // Compare each extracted frame hash to each training sample keyframe hash
                foreach (var frameHash in frameHashes)
                {
                    foreach (var keyframeHash in keyframeHashes)
                    {
                        var sampleHash = Convert.FromBase64String(keyframeHash.Hash);
                        var similarity = _photoHashService.CompareHashes(frameHash, sampleHash);

                        if (similarity > bestSimilarity)
                        {
                            bestSimilarity = similarity;
                            matchedSpamLabel = isSpam;
                        }
                    }
                }
            }

            // Check if similarity meets threshold
            if (bestSimilarity >= config.HashSimilarityThreshold)
            {
                logger.LogInformation(
                    "VideoSpam Layer 1: Keyframe hash match found ({Similarity:F2}% >= {Threshold:F2}%). Returning early with {Classification}",
                    bestSimilarity * 100, config.HashSimilarityThreshold * 100,
                    matchedSpamLabel == true ? "SPAM" : "CLEAN");

                return new ContentCheckResponse
                {
                    CheckName = CheckName,
                    Result = matchedSpamLabel == true ? CheckResultType.Spam : CheckResultType.Clean,
                    Details = $"Video keyframe {bestSimilarity:P0} similar to known {(matchedSpamLabel == true ? "spam" : "ham")} sample",
                    Confidence = config.HashMatchConfidence
                };
            }

            logger.LogDebug(
                "VideoSpam Layer 1: Best keyframe hash similarity {Similarity:F2}% below threshold {Threshold:F2}%, proceeding to OCR",
                bestSimilarity * 100, config.HashSimilarityThreshold * 100);

            return null; // Proceed to next layer
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in VideoSpam Layer 1 (keyframe hash similarity)");
            return null; // Proceed to next layer on error
        }
    }

    /// <summary>
    /// ML-6 Layer 2: OCR on frames + text-based spam detection
    /// </summary>
    private async Task<ContentCheckResponse?> CheckFrameOCRAsync(
        List<ExtractedFrame> frames,
        VideoSpamConfig config,
        VideoCheckRequest req,
        CancellationToken cancellationToken)
    {
        try
        {
            // Run OCR on all frames and collect text
            var combinedText = new List<string>();
            foreach (var frame in frames)
            {
                var extractedText = await _imageTextExtractionService.ExtractTextAsync(
                    frame.FramePath,
                    cancellationToken);

                if (!string.IsNullOrWhiteSpace(extractedText))
                {
                    combinedText.Add(extractedText);
                }
            }

            if (combinedText.Count == 0)
            {
                logger.LogDebug("VideoSpam Layer 2: No text extracted from video frames via OCR");
                return null;
            }

            var allText = string.Join(" ", combinedText);
            if (allText.Length < config.MinOcrTextLength)
            {
                logger.LogDebug("VideoSpam Layer 2: OCR extracted only {CharCount} characters (< {MinLength}), too short for text analysis",
                    allText.Length, config.MinOcrTextLength);
                return null;
            }

            logger.LogDebug(
                "VideoSpam Layer 2: OCR extracted {CharCount} characters from {FrameCount} frames, running text-based spam checks",
                allText.Length, combinedText.Count);

            // Create text-only request (no VideoLocalPath = VideoSpamCheck won't re-execute)
            var ocrRequest = new ContentCheckRequest
            {
                Message = allText,
                UserId = req.UserId,
                UserName = req.UserName,
                ChatId = req.ChatId,
                // Use defaults for properties not on VideoCheckRequest
                Metadata = new ContentCheckMetadata(),
                CheckOnly = false,
                HasSpamFlags = false,
                IsUserTrusted = false,
                IsUserAdmin = false,
                ImageData = null,
                PhotoFileId = null,
                PhotoUrl = null,
                PhotoLocalPath = null,
                VideoLocalPath = null,  // ← Key: No video = VideoSpamCheck won't run
                Urls = []
            };

            // Run all text-based spam checks on OCR text (includes OpenAI veto if needed)
            // Lazy-resolve engine to break circular dependency
            var contentDetectionEngine = _serviceProvider.GetRequiredService<IContentDetectionEngine>();
            var ocrResult = await contentDetectionEngine.CheckMessageAsync(ocrRequest, cancellationToken);

            // Check if result is confident enough to skip expensive Vision API
            if (ocrResult.MaxConfidence >= config.OcrConfidenceThreshold)
            {
                var flaggedChecks = ocrResult.CheckResults
                    .Where(c => c.Result == CheckResultType.Spam)
                    .Select(c => c.CheckName)
                    .ToList();

                logger.LogInformation(
                    "VideoSpam Layer 2: OCR text checks confident ({Confidence}% >= {Threshold}%), returning early with {Result}",
                    ocrResult.MaxConfidence, config.OcrConfidenceThreshold, ocrResult.IsSpam ? "SPAM" : "CLEAN");

                return new ContentCheckResponse
                {
                    CheckName = CheckName,
                    Result = ocrResult.IsSpam ? CheckResultType.Spam : CheckResultType.Clean,
                    Details = $"OCR from video frames analyzed by {flaggedChecks.Count} checks: {string.Join(", ", flaggedChecks)}",
                    Confidence = ocrResult.MaxConfidence
                };
            }

            // OCR checks uncertain - proceed to Vision (Layer 3)
            logger.LogDebug(
                "VideoSpam Layer 2: OCR text checks uncertain (confidence {Confidence}% < {Threshold}%), proceeding to Vision",
                ocrResult.MaxConfidence, config.OcrConfidenceThreshold);

            return null; // Proceed to next layer
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in VideoSpam Layer 2 (OCR + text checks)");
            return null; // Proceed to next layer on error
        }
    }

    /// <summary>
    /// ML-6 Layer 3: OpenAI Vision on representative frame
    /// </summary>
    private async Task<ContentCheckResponse> CheckFrameWithVisionAsync(
        List<ExtractedFrame> frames,
        VideoCheckRequest req,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrEmpty(req.ApiKey))
            {
                logger.LogWarning("OpenAI API key not configured for video spam detection");
                return new ContentCheckResponse
                {
                    CheckName = CheckName,
                    Result = CheckResultType.Clean,
                    Details = "OpenAI API key not configured",
                    Confidence = 0,
                    Error = new InvalidOperationException("OpenAI API key not configured")
                };
            }

            // Select best representative frame (prefer non-black frames, middle frame)
            var representativeFrame = frames
                .OrderBy(f => f.IsBlackFrame)  // Non-black first
                .ThenBy(f => Math.Abs(f.PositionPercent - 0.5))  // Closest to middle
                .First();

            logger.LogDebug("VideoSpam Layer 3: Using frame at {Position:P0} for Vision analysis",
                representativeFrame.PositionPercent);

            // Convert frame to base64 data URI for OpenAI Vision API
            var imageBytes = await File.ReadAllBytesAsync(representativeFrame.FramePath, cancellationToken);
            var base64 = Convert.ToBase64String(imageBytes);
            var imageUrl = $"data:image/jpeg;base64,{base64}";

            var prompt = BuildVideoPrompt(req.CustomPrompt);

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

            logger.LogDebug("VideoSpam Layer 3: Calling OpenAI Vision API for user {UserId}",
                req.UserId);

            // Use named "OpenAI" HttpClient (configured in ServiceCollectionExtensions)
            var httpClient = _httpClientFactory.CreateClient("OpenAI");
            var response = await httpClient.PostAsJsonAsync(
                "chat/completions",
                apiRequest,
                cancellationToken);

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
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
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

            var result = await response.Content.ReadFromJsonAsync<OpenAIVisionApiResponse>(cancellationToken: cancellationToken);
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
            logger.LogError(ex, "Error in VideoSpam Layer 3 (OpenAI Vision)");
            return new ContentCheckResponse
            {
                CheckName = CheckName,
                Result = CheckResultType.Clean, // Fail open
                Details = "Vision API error",
                Confidence = 0,
                Error = ex
            };
        }
    }

    /// <summary>
    /// Build prompt for OpenAI Vision analysis of video frame
    /// </summary>
    private static string BuildVideoPrompt(string? customPrompt)
    {
        // Use custom prompt if provided, otherwise use default
        var systemPrompt = customPrompt ?? GetDefaultVideoPrompt();

        return $$"""
            {{systemPrompt}}

            Note: This is a keyframe extracted from a video message. Analyze the visual content.

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
    /// Get default video spam detection prompt (used when CustomPrompt is not configured)
    /// </summary>
    private static string GetDefaultVideoPrompt() => """
        You are a spam detection system for Telegram. Analyze this video frame and determine if it's spam/scam.

        Common Telegram video spam patterns:
        1. Crypto airdrop scams (fake celebrity accounts, "send X get Y back", urgent claims)
        2. Phishing - fake wallet UIs, transaction confirmations, login screens
        3. Airdrop scams with urgent language ("claim now", "limited time") + ticker symbols ($TOKEN, $COIN)
        4. Impersonation - fake verified accounts, customer support
        5. Get-rich-quick schemes ("earn daily", "passive income", guaranteed profits)
        6. Screenshots of fake transactions or social media posts
        7. Referral spam with voucher codes or suspicious links
        8. Promotional videos with excessive text overlays promoting scams

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

    /// <summary>
    /// Clean up extracted frame files
    /// </summary>
    private void CleanupFrames(List<ExtractedFrame> frames)
    {
        foreach (var frame in frames)
        {
            try
            {
                if (File.Exists(frame.FramePath))
                {
                    File.Delete(frame.FramePath);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to clean up extracted frame at {FramePath}", frame.FramePath);
            }
        }
    }
}

/// <summary>
/// Keyframe hash JSON structure for deserialization from video_training_samples.keyframe_hashes
/// </summary>
internal record KeyframeHashJson(
    [property: JsonPropertyName("position")] double Position,
    [property: JsonPropertyName("hash")] string Hash
);
