using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.SpamDetection.Abstractions;
using TelegramGroupsAdmin.SpamDetection.Models;

namespace TelegramGroupsAdmin.SpamDetection.Checks;

/// <summary>
/// Spam check using CAS (Combot Anti-Spam) API to check if user is in global spam database
/// Based on tg-spam's CAS integration
/// </summary>
public class CasSpamCheck : ISpamCheck
{
    private readonly ILogger<CasSpamCheck> _logger;
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;

    public string CheckName => "CAS";

    public CasSpamCheck(
        ILogger<CasSpamCheck> logger,
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache)
    {
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();
        _cache = cache;
    }

    /// <summary>
    /// Check if CAS check should be executed
    /// </summary>
    public bool ShouldExecute(SpamCheckRequest request)
    {
        // Need user ID for CAS lookup
        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Execute CAS spam check with strongly-typed request
    /// Config comes from request - no database access needed
    /// </summary>
    public async Task<SpamCheckResponse> CheckAsync(SpamCheckRequestBase request)
    {
        var req = (CasCheckRequest)request;

        try
        {
            var cacheKey = $"cas_check_{req.UserId}";

            // Check cache first
            if (_cache.TryGetValue(cacheKey, out CasResponse? cachedResponse) && cachedResponse != null)
            {
                _logger.LogDebug("CAS check for user {UserId}: Using cached result", req.UserId);
                return CreateResponse(cachedResponse, fromCache: true);
            }

            // Make API request to CAS with timeout
            var apiUrl = $"{req.ApiUrl.TrimEnd('/')}/check?user_id={req.UserId}";
            _logger.LogDebug("CAS check for user {UserId}: Calling {ApiUrl}", req.UserId, apiUrl);

            // Create cancellation token with timeout (can't modify HttpClient.Timeout after first use)
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(req.CancellationToken);
            timeoutCts.CancelAfter(req.Timeout);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            if (!string.IsNullOrEmpty(req.UserAgent))
            {
                httpRequest.Headers.Add("User-Agent", req.UserAgent);
            }

            using var response = await _httpClient.SendAsync(httpRequest, timeoutCts.Token);

            // Fail open on any error
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("CAS API returned {StatusCode} for user {UserId}", response.StatusCode, req.UserId);
                return CreateFailResponse("CAS API error");
            }

            var jsonContent = await response.Content.ReadAsStringAsync(req.CancellationToken);
            var casResponse = JsonSerializer.Deserialize<CasResponse>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (casResponse == null)
            {
                _logger.LogWarning("Failed to parse CAS API response for user {UserId}", req.UserId);
                return CreateFailResponse("Failed to parse CAS response");
            }

            // Cache the result for 1 hour
            _cache.Set(cacheKey, casResponse, TimeSpan.FromHours(1));

            return CreateResponse(casResponse, fromCache: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CAS check failed for user {UserId}", req.UserId);
            return CreateFailResponse("CAS check failed due to error", ex);
        }
    }

    /// <summary>
    /// Create spam check response from CAS API response
    /// </summary>
    private SpamCheckResponse CreateResponse(CasResponse casResponse, bool fromCache)
    {
        var isSpam = casResponse.Ok && casResponse.Result?.IsBanned == true;
        var result = isSpam ? SpamCheckResultType.Spam : SpamCheckResultType.Clean;
        var confidence = isSpam ? 95 : 0; // High confidence if CAS says user is banned

        var details = isSpam
            ? $"User banned in CAS database{(fromCache ? " (cached)" : "")}"
            : $"User not found in CAS database{(fromCache ? " (cached)" : "")}";

        _logger.LogDebug("CAS check completed: IsSpam={IsSpam}, Confidence={Confidence}, FromCache={FromCache}",
            isSpam, confidence, fromCache);

        return new SpamCheckResponse
        {
            CheckName = CheckName,
            Result = result,
            Details = details,
            Confidence = confidence
        };
    }

    /// <summary>
    /// Create failure response (fail open)
    /// </summary>
    private SpamCheckResponse CreateFailResponse(string details, Exception? error = null)
    {
        return new SpamCheckResponse
        {
            CheckName = CheckName,
            Result = SpamCheckResultType.Clean, // Fail open
            Details = details,
            Confidence = 0,
            Error = error
        };
    }

    /// <summary>
    /// CAS API response structure
    /// </summary>
    private record CasResponse
    {
        public bool Ok { get; init; }
        public CasResult? Result { get; init; }
    }

    /// <summary>
    /// CAS API result structure
    /// </summary>
    private record CasResult
    {
        public string? UserId { get; init; }
        public bool IsBanned { get; init; }
        public string[]? Messages { get; init; }
    }
}