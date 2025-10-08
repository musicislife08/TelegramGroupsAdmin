using Microsoft.Extensions.Options;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Data.Repositories;
using TelegramGroupsAdmin.Services.Telegram;
using TelegramGroupsAdmin.Services.Vision;

namespace TelegramGroupsAdmin.Endpoints;

public static class SpamCheckEndpoints
{
    public static void MapSpamCheckEndpoints(this WebApplication app)
    {
        // API endpoint: Spam check
        app.MapPost("/check", async (
            HttpContext httpContext,
            SpamCheckRequest request,
            SpamCheckService textSpamService,
            MessageHistoryRepository historyRepo,
            SpamCheckRepository spamCheckRepo,
            ITelegramImageService imageService,
            IVisionSpamDetectionService visionService,
            IOptions<SpamDetectionOptions> spamOptions,
            ILogger<Program> logger) =>
        {
            // Validate API key (support both header and query string for different systems)
            var apiKey = httpContext.Request.Headers["X-API-Key"].FirstOrDefault()
                ?? httpContext.Request.Query["api_key"].FirstOrDefault();
            var expectedKey = spamOptions.Value.ApiKey;

            if (string.IsNullOrEmpty(expectedKey))
            {
                logger.LogWarning("SPAMDETECTION__APIKEY not configured. /check endpoint is unprotected!");
            }
            else if (apiKey != expectedKey)
            {
                logger.LogWarning("Unauthorized /check request with invalid API key from {RemoteIp}", httpContext.Connection.RemoteIpAddress);
                return Results.Unauthorized();
            }

            // Check for image spam
            if (request.ImageCount > 0)
            {
                logger.LogInformation("Processing image spam check for user {UserId} in chat {ChatId}", request.UserId, request.ChatId);

                // Try to find user's recent photo
                var photoMessage = await historyRepo.GetUserRecentPhotoAsync(long.Parse(request.UserId), long.Parse(request.ChatId));

                // Retry once if not found (race condition handling)
                if (photoMessage == null)
                {
                    logger.LogDebug("Photo not found on first attempt, retrying after {Delay}ms", spamOptions.Value.ImageLookupRetryDelayMs);
                    await Task.Delay(spamOptions.Value.ImageLookupRetryDelayMs);
                    photoMessage = await historyRepo.GetUserRecentPhotoAsync(long.Parse(request.UserId), long.Parse(request.ChatId));
                }

                if (photoMessage == null)
                {
                    logger.LogWarning("No recent photo found for user {UserId} in chat {ChatId}", request.UserId, request.ChatId);
                    return Results.Ok(new CheckResult(false, "No recent image found", 0));
                }

                // Download image
                var imageStream = await imageService.DownloadPhotoAsync(photoMessage.FileId);
                if (imageStream == null)
                {
                    logger.LogError("Failed to download photo {FileId} for user {UserId}", photoMessage.FileId, request.UserId);
                    return Results.Ok(new CheckResult(false, "Failed to download image", 0));
                }

                // Analyze with OpenAI Vision
                var result = await visionService.AnalyzeImageAsync(
                    imageStream,
                    request.Message ?? photoMessage.MessageText);

                logger.LogInformation(
                    "Image spam check complete for user {UserId}: Spam={Spam}, Confidence={Confidence}",
                    request.UserId,
                    result.Spam,
                    result.Confidence);

                // Persist spam check result
                var checkTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var contentHash = photoMessage.MessageText != null
                    ? ComputeContentHash(photoMessage.MessageText, "")
                    : null;

                // Try to find matched message ID
                long? matchedMessageId = null;
                if (!string.IsNullOrEmpty(contentHash))
                {
                    matchedMessageId = await spamCheckRepo.FindMatchedMessageIdAsync(contentHash, checkTimestamp);
                }

                // Fallback: match by user and timestamp
                if (!matchedMessageId.HasValue)
                {
                    matchedMessageId = await spamCheckRepo.FindMessageByUserAndTimeAsync(
                        long.Parse(request.UserId),
                        checkTimestamp);
                }

                var spamCheck = new SpamCheckRecord(
                    Id: 0, // Will be set by INSERT
                    CheckTimestamp: checkTimestamp,
                    UserId: long.Parse(request.UserId),
                    ContentHash: contentHash,
                    IsSpam: result.Spam,
                    Confidence: result.Confidence,
                    Reason: result.Reason,
                    CheckType: "vision",
                    MatchedMessageId: matchedMessageId
                );

                await spamCheckRepo.InsertSpamCheckAsync(spamCheck);

                return Results.Ok(result);
            }

            // Check for text spam (existing logic)
            if (!string.IsNullOrWhiteSpace(request.Message))
            {
                logger.LogInformation("Processing text spam check");
                var result = await textSpamService.CheckMessageAsync(request.Message);

                // Persist spam check result
                var checkTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var urls = ExtractUrls(request.Message);
                var contentHash = ComputeContentHash(request.Message, urls);

                // Try to find matched message ID
                var matchedMessageId = await spamCheckRepo.FindMatchedMessageIdAsync(contentHash, checkTimestamp);

                // Fallback: match by user and timestamp
                if (!matchedMessageId.HasValue && !string.IsNullOrEmpty(request.UserId))
                {
                    matchedMessageId = await spamCheckRepo.FindMessageByUserAndTimeAsync(
                        long.Parse(request.UserId),
                        checkTimestamp);
                }

                var spamCheck = new SpamCheckRecord(
                    Id: 0, // Will be set by INSERT
                    CheckTimestamp: checkTimestamp,
                    UserId: !string.IsNullOrEmpty(request.UserId) ? long.Parse(request.UserId) : 0,
                    ContentHash: contentHash,
                    IsSpam: result.Spam,
                    Confidence: result.Confidence,
                    Reason: result.Reason,
                    CheckType: "text",
                    MatchedMessageId: matchedMessageId
                );

                await spamCheckRepo.InsertSpamCheckAsync(spamCheck);

                return Results.Ok(result);
            }

            // Empty message
            return Results.Ok(new CheckResult(false, "Empty message", 0));
        });

        static string ComputeContentHash(string messageText, string urls)
        {
            var normalized = $"{messageText?.ToLowerInvariant().Trim() ?? ""}{urls?.ToLowerInvariant().Trim() ?? ""}";
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(normalized));
            return Convert.ToHexString(hashBytes);
        }

        static string ExtractUrls(string message)
        {
            var urlPattern = @"https?://[^\s]+";
            var matches = System.Text.RegularExpressions.Regex.Matches(message, urlPattern);
            return string.Join(",", matches.Select(m => m.Value));
        }

        // API endpoint: Health check
        app.MapGet("/health", async (MessageHistoryRepository historyRepo) =>
        {
            var stats = await historyRepo.GetStatsAsync();
            return Results.Ok(new
            {
                status = "healthy",
                historyBot = new
                {
                    totalMessages = stats.TotalMessages,
                    totalUsers = stats.UniqueUsers,
                    messagesWithPhotos = stats.PhotoCount,
                    oldestMessage = stats.OldestTimestamp.HasValue
                        ? DateTimeOffset.FromUnixTimeSeconds(stats.OldestTimestamp.Value).ToString("g")
                        : null,
                    newestMessage = stats.NewestTimestamp.HasValue
                        ? DateTimeOffset.FromUnixTimeSeconds(stats.NewestTimestamp.Value).ToString("g")
                        : null
                }
            });
        });

        // Logout endpoint
        app.MapGet("/logout", async (HttpContext context) =>
        {
            await Microsoft.AspNetCore.Authentication.AuthenticationHttpContextExtensions.SignOutAsync(
                context, Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/login");
        });
    }
}
