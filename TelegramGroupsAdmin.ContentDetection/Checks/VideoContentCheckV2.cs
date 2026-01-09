using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.ContentDetection.Abstractions;
using TelegramGroupsAdmin.Configuration.Models.ContentDetection;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Helpers;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Core.Services.AI;

namespace TelegramGroupsAdmin.ContentDetection.Checks;

/// <summary>
/// Spam check that analyzes videos using 3-layer detection strategy (ML-6).
/// Provider-agnostic - uses IChatService for multi-provider Vision support.
/// Layer 1: Keyframe hash similarity (fastest, cheapest, most reliable for known spam)
/// Layer 2: OCR on frames + text spam checks (fast, cheap, good for text-heavy videos)
/// Layer 3: AI Vision on frames fallback (slow, expensive, comprehensive)
/// </summary>
public class VideoContentCheckV2(
    ILogger<VideoContentCheckV2> logger,
    IChatService chatService,
    IVideoFrameExtractionService frameExtractionService,
    IImageTextExtractionService imageTextExtractionService,
    IServiceProvider serviceProvider,
    IContentDetectionConfigRepository configRepository,
    IPhotoHashService photoHashService,
    IVideoTrainingSamplesRepository videoTrainingSamplesRepository) : IContentCheckV2
{
    // Lazy resolve to break circular dependency

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
    public async ValueTask<ContentCheckResponseV2> CheckAsync(ContentCheckRequestBase request)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        var req = (VideoCheckRequest)request;

        try
        {
            // Verify FFmpeg is available
            if (!frameExtractionService.IsAvailable)
            {
                logger.LogWarning("FFmpeg not available, cannot analyze video");
                return new ContentCheckResponseV2
                {
                    CheckName = CheckName,
                    Score = 0.0,
                    Abstained = true,
                    Details = "FFmpeg not available for video analysis",
                    Error = new InvalidOperationException("FFmpeg not available"),
                    ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
                };
            }

            // Verify video file exists
            if (!File.Exists(req.VideoLocalPath))
            {
                logger.LogWarning("Video file not found at {VideoPath}", req.VideoLocalPath);
                return new ContentCheckResponseV2
                {
                    CheckName = CheckName,
                    Score = 0.0,
                    Abstained = true,
                    Details = "Video file not found",
                    Error = new FileNotFoundException("Video file not found", req.VideoLocalPath),
                    ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
                };
            }

            // Load config
            var config = await configRepository.GetEffectiveConfigAsync(req.ChatId, req.CancellationToken);
            var videoConfig = config.VideoSpam;

            // Extract keyframes from video
            var frames = await frameExtractionService.ExtractKeyframesAsync(req.VideoLocalPath, req.CancellationToken);
            if (frames.Count == 0)
            {
                logger.LogWarning("Failed to extract keyframes from video at {VideoPath}", req.VideoLocalPath);
                return new ContentCheckResponseV2
                {
                    CheckName = CheckName,
                    Score = 0.0,
                    Abstained = true,
                    Details = "Failed to extract video frames",
                    Error = new InvalidOperationException("Failed to extract video frames"),
                    ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
                };
            }

            logger.LogDebug("Extracted {FrameCount} keyframes from video at {VideoPath}",
                frames.Count, Path.GetFileName(req.VideoLocalPath));

            // ML-6 Layer 1: Keyframe hash similarity check (fastest - check if we've seen this spam before)
            if (videoConfig.UseHashSimilarity)
            {
                var layer1Result = await CheckKeyframeHashSimilarityAsync(frames, videoConfig, startTimestamp, req.CancellationToken);
                if (layer1Result != null)
                {
                    // Clean up extracted frames
                    CleanupFrames(frames);
                    return layer1Result;
                }
            }

            // ML-6 Layer 2: OCR on frames + text-based spam detection
            if (videoConfig.UseOCR && imageTextExtractionService.IsAvailable)
            {
                var layer2Result = await CheckFrameOCRAsync(frames, videoConfig, req, startTimestamp, req.CancellationToken);
                if (layer2Result != null)
                {
                    // Clean up extracted frames
                    CleanupFrames(frames);
                    return layer2Result;
                }
            }

            // ML-6 Layer 3: AI Vision fallback on representative frame
            if (videoConfig.UseOpenAIVision)
            {
                var layer3Result = await CheckFrameWithVisionAsync(frames, req, startTimestamp, req.CancellationToken);

                // Clean up extracted frames
                CleanupFrames(frames);

                return layer3Result;
            }

            // No layers enabled or all failed - clean up and return clean
            CleanupFrames(frames);
            return new ContentCheckResponseV2
            {
                CheckName = CheckName,
                Score = 0.0,
                Abstained = true,
                Details = "Video spam detection disabled or all layers failed",
                ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in VideoSpamCheckV2 for user {UserId}", req.UserId);
            return new ContentCheckResponseV2
            {
                CheckName = CheckName,
                Score = 0.0,
                Abstained = true,
                Details = "Video spam check failed",
                Error = ex,
                ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
            };
        }
    }

    /// <summary>
    /// ML-6 Layer 1: Check keyframe hash similarity against training samples
    /// </summary>
    private async Task<ContentCheckResponseV2?> CheckKeyframeHashSimilarityAsync(
        List<ExtractedFrame> frames,
        VideoContentConfig config,
        long startTimestamp,
        CancellationToken cancellationToken)
    {
        try
        {
            // Compute hashes for all extracted frames (pre-allocate capacity to avoid resizing)
            var frameHashes = new List<byte[]>(frames.Count);
            foreach (var frame in frames)
            {
                var hash = await photoHashService.ComputePhotoHashAsync(frame.FramePath);
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
            var trainingSamples = await videoTrainingSamplesRepository.GetRecentSamplesAsync(
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
                        var similarity = photoHashService.CompareHashes(frameHash, sampleHash);

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

                // Convert confidence to score: spam gets full score, clean abstains
                double score;
                bool abstained;
                if (matchedSpamLabel == true)
                {
                    // Spam: map confidence (0-100) to score (0-5.0)
                    score = (config.HashMatchConfidence / 100.0) * AIConstants.ConfidenceToScoreMultiplier;
                    abstained = false;
                }
                else
                {
                    // Clean: abstain
                    score = 0.0;
                    abstained = true;
                }

                return new ContentCheckResponseV2
                {
                    CheckName = CheckName,
                    Score = score,
                    Abstained = abstained,
                    Details = $"Video keyframe {bestSimilarity:P0} similar to known {(matchedSpamLabel == true ? "spam" : "ham")} sample",
                    ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
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
    private async Task<ContentCheckResponseV2?> CheckFrameOCRAsync(
        List<ExtractedFrame> frames,
        VideoContentConfig config,
        VideoCheckRequest req,
        long startTimestamp,
        CancellationToken cancellationToken)
    {
        try
        {
            // Run OCR on all frames and collect text
            var combinedText = new List<string>();
            foreach (var frame in frames)
            {
                var extractedText = await imageTextExtractionService.ExtractTextAsync(
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
            var contentDetectionEngine = serviceProvider.GetRequiredService<IContentDetectionEngine>();
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

                // Convert confidence to score: spam gets full score, clean abstains
                double score;
                bool abstained;
                if (ocrResult.IsSpam)
                {
                    // Spam: map confidence (0-100) to score (0-5.0)
                    score = (ocrResult.MaxConfidence / 100.0) * AIConstants.ConfidenceToScoreMultiplier;
                    abstained = false;
                }
                else
                {
                    // Clean: abstain
                    score = 0.0;
                    abstained = true;
                }

                return new ContentCheckResponseV2
                {
                    CheckName = CheckName,
                    Score = score,
                    Abstained = abstained,
                    Details = $"OCR from video frames analyzed by {flaggedChecks.Count} checks: {string.Join(", ", flaggedChecks)}",
                    ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
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
    /// ML-6 Layer 3: AI Vision on representative frame (provider-agnostic via IChatService)
    /// </summary>
    private async Task<ContentCheckResponseV2> CheckFrameWithVisionAsync(
        List<ExtractedFrame> frames,
        VideoCheckRequest req,
        long startTimestamp,
        CancellationToken cancellationToken)
    {
        try
        {
            // Check if video analysis feature is available
            if (!await chatService.IsFeatureAvailableAsync(AIFeatureType.VideoAnalysis, cancellationToken))
            {
                logger.LogDebug("VideoSpam Layer 3: Vision not configured - no AI provider for video analysis");
                return new ContentCheckResponseV2
                {
                    CheckName = CheckName,
                    Score = 0.0,
                    Abstained = true,
                    Details = "Vision not available - no AI provider configured for video analysis",
                    ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
                };
            }

            // Select best representative frame (prefer non-black frames, middle frame)
            var representativeFrame = frames
                .OrderBy(f => f.IsBlackFrame)  // Non-black first
                .ThenBy(f => Math.Abs(f.PositionPercent - 0.5))  // Closest to middle
                .First();

            logger.LogDebug("VideoSpam Layer 3: Using frame at {Position:P0} for Vision analysis",
                representativeFrame.PositionPercent);

            // Load frame image data
            var imageData = await File.ReadAllBytesAsync(representativeFrame.FramePath, cancellationToken);

            var prompt = BuildVideoPrompt(req.CustomPrompt);

            logger.LogDebug("VideoSpam Layer 3: Calling AI Vision API for user {UserId}", req.UserId);

            // Temperature uses feature config default (set in AI Integration settings)
            var result = await chatService.GetVisionCompletionAsync(
                AIFeatureType.VideoAnalysis,
                GetDefaultVideoPrompt(),
                prompt,
                imageData,
                "image/jpeg",
                new ChatCompletionOptions
                {
                    MaxTokens = AIConstants.VideoVisionMaxTokens
                },
                cancellationToken);

            if (result == null || string.IsNullOrWhiteSpace(result.Content))
            {
                logger.LogWarning("Empty response from AI Vision for user {UserId}", req.UserId);
                return new ContentCheckResponseV2
                {
                    CheckName = CheckName,
                    Score = 0.0,
                    Abstained = true,
                    Details = "Empty AI Vision response",
                    Error = new InvalidOperationException("Empty AI Vision response"),
                    ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
                };
            }

            return ParseSpamResponse(result.Content, req.UserId, startTimestamp);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in VideoSpam Layer 3 (AI Vision)");
            return new ContentCheckResponseV2
            {
                CheckName = CheckName,
                Score = 0.0,
                Abstained = true,
                Details = "Vision API error",
                Error = ex,
                ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
            };
        }
    }

    /// <summary>
    /// Build prompt for AI Vision analysis of video frame
    /// </summary>
    private static string BuildVideoPrompt(string? customPrompt)
    {
        return """
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
    /// Get default video spam detection prompt (used as system prompt for Vision)
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
    /// Parse AI Vision response and create spam check result
    /// </summary>
    private ContentCheckResponseV2 ParseSpamResponse(string content, long userId, long startTimestamp)
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

            var response = JsonSerializer.Deserialize<VideoSpamResponse>(
                content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (response == null)
            {
                logger.LogWarning("Failed to deserialize AI Vision response for user {UserId}: {Content}",
                    userId, content);
                return new ContentCheckResponseV2
                {
                    CheckName = CheckName,
                    Score = 0.0,
                    Abstained = true,
                    Details = "Failed to parse AI Vision response",
                    Error = new InvalidOperationException("Failed to parse AI Vision response"),
                    ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
                };
            }

            logger.LogDebug("AI Vision analysis for user {UserId}: Spam={Spam}, Confidence={Confidence}, Reason={Reason}",
                userId, response.Spam, response.Confidence, response.Reason);

            var details = response.Reason ?? "No reason provided";
            if (response.PatternsDetected?.Length > 0)
            {
                details += $" (Patterns: {string.Join(", ", response.PatternsDetected)})";
            }

            // Convert confidence to score: spam gets full score, clean abstains
            double score;
            bool abstained;
            if (response.Spam)
            {
                // Spam: map confidence (0-100) to score (0-5.0)
                score = (response.Confidence / 100.0) * AIConstants.ConfidenceToScoreMultiplier;
                abstained = false;
            }
            else
            {
                // Clean: abstain
                score = 0.0;
                abstained = true;
            }

            return new ContentCheckResponseV2
            {
                CheckName = CheckName,
                Score = score,
                Abstained = abstained,
                Details = details,
                ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error parsing AI Vision response for user {UserId}: {Content}",
                userId, content);
            return new ContentCheckResponseV2
            {
                CheckName = CheckName,
                Score = 0.0,
                Abstained = true,
                Details = "Failed to parse spam analysis",
                Error = ex,
                ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
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

/// <summary>
/// Expected JSON response structure from AI Vision for video spam detection.
/// This is the format we request in our prompts.
/// </summary>
internal record VideoSpamResponse(
    [property: JsonPropertyName("spam")] bool Spam,
    [property: JsonPropertyName("confidence")] int Confidence,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("patterns_detected")] string[]? PatternsDetected
);
