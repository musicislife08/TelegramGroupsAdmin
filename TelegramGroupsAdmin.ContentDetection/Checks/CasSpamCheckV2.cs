using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Abstractions;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Checks;

/// <summary>
/// V2 CAS check with proper abstention
/// Key changes:
/// - Abstain when not found (instead of Clean 0%)
/// - Return 5.0 points when banned (user requested - very strong signal)
/// - Abstain on failure (fail open)
/// </summary>
public class CasSpamCheckV2(
    ILogger<CasSpamCheckV2> logger,
    IHttpClientFactory httpClientFactory,
    IMemoryCache cache) : IContentCheckV2
{
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient();

    // User requested: CAS = 5.0 points (very strong signal, same as Bayes 99%)
    private const double ScoreCasBanned = 5.0;

    public CheckName CheckName => CheckName.CAS;

    public bool ShouldExecute(ContentCheckRequest request)
    {
        return request.UserId != 0;
    }

    public async ValueTask<ContentCheckResponseV2> CheckAsync(ContentCheckRequestBase request)
    {
        var req = (CasCheckRequest)request;

        try
        {
            var cacheKey = $"cas_check_{req.UserId}";

            // Check cache first
            if (cache.TryGetValue(cacheKey, out CasResponse? cachedResponse) && cachedResponse != null)
            {
                logger.LogDebug("CAS V2 check for user {UserId}: Using cached result", req.UserId);
                return CreateResponse(cachedResponse, fromCache: true);
            }

            // Make API request
            var apiUrl = $"{req.ApiUrl.TrimEnd('/')}/check?user_id={req.UserId}";
            logger.LogDebug("CAS V2 check for user {UserId}: Calling {ApiUrl}", req.UserId, apiUrl);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(req.CancellationToken);
            timeoutCts.CancelAfter(req.Timeout);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            if (!string.IsNullOrEmpty(req.UserAgent))
            {
                httpRequest.Headers.Add("User-Agent", req.UserAgent);
            }

            using var response = await _httpClient.SendAsync(httpRequest, timeoutCts.Token);

            // Fail open on error
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("CAS API returned {StatusCode} for user {UserId}", response.StatusCode, req.UserId);
                return CreateFailResponse("CAS API error");
            }

            var jsonContent = await response.Content.ReadAsStringAsync(req.CancellationToken);
            var casResponse = JsonSerializer.Deserialize<CasResponse>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (casResponse == null)
            {
                logger.LogWarning("Failed to parse CAS API response for user {UserId}", req.UserId);
                return CreateFailResponse("Failed to parse CAS response");
            }

            // Cache for 1 hour
            cache.Set(cacheKey, casResponse, TimeSpan.FromHours(1));

            return CreateResponse(casResponse, fromCache: false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in CasSpamCheckV2 for user {UserId}", req.UserId);
            return new ContentCheckResponseV2
            {
                CheckName = CheckName,
                Score = 0.0,
                Abstained = true,
                Details = $"Error: {ex.Message}",
                Error = ex
            };
        }
    }

    private ContentCheckResponseV2 CreateResponse(CasResponse casResponse, bool fromCache)
    {
        var isBanned = casResponse.Ok && casResponse.Result?.IsBanned == true;

        if (isBanned)
        {
            // V2: CAS banned = 5.0 points (very strong signal)
            return new ContentCheckResponseV2
            {
                CheckName = CheckName,
                Score = ScoreCasBanned,
                Abstained = false,
                Details = $"User banned in CAS database{(fromCache ? " (cached)" : "")}"
            };
        }

        // V2 FIX: Not found = abstain (not "Clean 0%")
        return new ContentCheckResponseV2
        {
            CheckName = CheckName,
            Score = 0.0,
            Abstained = true,
            Details = $"User not found in CAS database{(fromCache ? " (cached)" : "")}"
        };
    }

    private ContentCheckResponseV2 CreateFailResponse(string details, Exception? error = null)
    {
        // V2: Fail open = abstain
        return new ContentCheckResponseV2
        {
            CheckName = CheckName,
            Score = 0.0,
            Abstained = true,
            Details = details,
            Error = error
        };
    }

    private record CasResponse
    {
        public bool Ok { get; init; }
        public CasResult? Result { get; init; }
    }

    private record CasResult
    {
        public string? UserId { get; init; }
        public bool IsBanned { get; init; }
        public string[]? Messages { get; init; }
    }
}
