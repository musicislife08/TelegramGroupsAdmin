using Microsoft.Extensions.Options;
using TelegramGroupsAdmin.Configuration;
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
            SpamCheckRequest request,
            SpamCheckService textSpamService,
            MessageHistoryRepository historyRepo,
            ITelegramImageService imageService,
            IVisionSpamDetectionService visionService,
            IOptions<SpamDetectionOptions> spamOptions,
            ILogger<Program> logger) =>
        {
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

                return Results.Ok(result);
            }

            // Check for text spam (existing logic)
            if (!string.IsNullOrWhiteSpace(request.Message))
            {
                logger.LogInformation("Processing text spam check");
                var result = await textSpamService.CheckMessageAsync(request.Message);
                return Results.Ok(result);
            }

            // Empty message
            return Results.Ok(new CheckResult(false, "Empty message", 0));
        });

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
