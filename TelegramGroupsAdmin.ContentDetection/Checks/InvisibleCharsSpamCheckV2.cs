using TelegramGroupsAdmin.ContentDetection.Abstractions;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Helpers;
using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Checks;

/// <summary>
/// V2 invisible chars check with proper abstention
/// Key fix: Abstain when no invisible chars (not Clean 20%)
/// Scoring: 1.5 points when found (research guidance)
/// </summary>
public class InvisibleCharsSpamCheckV2 : IContentCheckV2
{
    private const double ScoreInvisibleChars = 1.5; // Research: heuristics = 1.5 points

    public CheckName CheckName => CheckName.InvisibleChars;

    public bool ShouldExecute(ContentCheckRequest request)
    {
        return !string.IsNullOrWhiteSpace(request.Message);
    }

    public ValueTask<ContentCheckResponseV2> CheckAsync(ContentCheckRequestBase request)
    {
        var req = (InvisibleCharsCheckRequest)request;

        try
        {
            var (hasInvisibleChars, count) = CheckForInvisibleCharacters(req.Message);

            if (hasInvisibleChars)
            {
                return ValueTask.FromResult(new ContentCheckResponseV2
                {
                    CheckName = CheckName,
                    Score = ScoreInvisibleChars,
                    Abstained = false,
                    Details = $"Contains {count} invisible/hidden characters"
                });
            }

            // V2 FIX: Abstain when no invisible chars (not Clean 20%)
            return ValueTask.FromResult(new ContentCheckResponseV2
            {
                CheckName = CheckName,
                Score = 0.0,
                Abstained = true,
                Details = "No invisible characters detected"
            });
        }
        catch (Exception ex)
        {
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

    private static (bool hasInvisibleChars, int count) CheckForInvisibleCharacters(string message)
    {
        // Check for invisible/zero-width characters
        var count = message.Count(c => c == '\u200B' || c == '\u200C' || c == '\u200D' || c == '\uFEFF');
        return (count > 0, count);
    }
}
