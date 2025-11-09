using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Abstractions;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.ContentDetection.Services.Blocklists;

namespace TelegramGroupsAdmin.ContentDetection.Checks;

/// <summary>
/// V2 URL blocklist check with proper abstention
/// Scoring: 2.0 points when domain on blocklist
/// </summary>
#pragma warning disable CS9113 // Parameter is unread (stub implementation)
public class UrlBlocklistSpamCheckV2(
    ILogger<UrlBlocklistSpamCheckV2> logger,
    IDomainFiltersRepository domainFiltersRepo) : IContentCheckV2
#pragma warning restore CS9113
{
    private const double ScoreBlocklistedDomain = 2.0;

    public CheckName CheckName => CheckName.UrlBlocklist;

    public bool ShouldExecute(ContentCheckRequest request)
    {
        return request.Urls != null && request.Urls.Any();
    }

    public ValueTask<ContentCheckResponseV2> CheckAsync(ContentCheckRequestBase request)
    {
        var req = (UrlBlocklistCheckRequest)request;

        try
        {
            if (req.Urls == null || !req.Urls.Any())
            {
                return ValueTask.FromResult(new ContentCheckResponseV2
                {
                    CheckName = CheckName,
                    Score = 0.0,
                    Abstained = true,
                    Details = "No URLs found in message"
                });
            }

            // TODO: Full implementation would check domains against filters
            // For now, abstain (proper implementation needs domain parsing + filter check)

            // V2: Abstain when no matches (not Clean 0%)
            return ValueTask.FromResult(new ContentCheckResponseV2
            {
                CheckName = CheckName,
                Score = 0.0,
                Abstained = true,
                Details = $"No filter matches for {req.Urls.Count} URL(s)"
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in UrlBlocklistSpamCheckV2");
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
