using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Abstractions;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Checks;

/// <summary>
/// V2 image spam check with proper abstention
/// Scoring: Map OpenAI confidence to 0-5.0 points
/// Note: Implementation placeholder - full OpenAI Vision integration would be complex
/// </summary>
public class ImageSpamCheckV2(ILogger<ImageSpamCheckV2> logger) : IContentCheckV2
{
    public CheckName CheckName => CheckName.ImageSpam;

    public bool ShouldExecute(ContentCheckRequest request)
    {
        return !string.IsNullOrEmpty(request.PhotoFileId) || !string.IsNullOrEmpty(request.PhotoLocalPath);
    }

    public ValueTask<ContentCheckResponseV2> CheckAsync(ContentCheckRequestBase request)
    {
        var req = (ImageCheckRequest)request;

        try
        {
            // TODO: Full implementation would call OpenAI Vision API
            // For now, abstain

            return ValueTask.FromResult(new ContentCheckResponseV2
            {
                CheckName = CheckName,
                Score = 0.0,
                Abstained = true,
                Details = "Image spam check not fully implemented in V2"
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in ImageSpamCheckV2");
            return ValueTask.FromResult(new ContentCheckResponseV2
            {
                CheckName = CheckName,
                Score = 0.0,
                Abstained = true,
                Details = $"Error: {ex.Message}",
                Error = ex
            });
        }
    }
}
