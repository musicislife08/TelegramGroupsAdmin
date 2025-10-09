using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.SpamDetection.Abstractions;
using TelegramGroupsAdmin.SpamDetection.Configuration;
using TelegramGroupsAdmin.SpamDetection.Models;

namespace TelegramGroupsAdmin.SpamDetection.Checks;

/// <summary>
/// Spam check using CAS (Combot Anti-Spam) API to check if user is in global spam database
/// Based on tg-spam's CAS integration
/// </summary>
public class CasSpamCheck : ISpamCheck
{
    private readonly ILogger<CasSpamCheck> _logger;
    private readonly SpamDetectionConfig _config;
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;

    public string CheckName => "CAS";

    public CasSpamCheck(
        ILogger<CasSpamCheck> logger,
        SpamDetectionConfig config,
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache)
    {
        _logger = logger;
        _config = config;
        _httpClient = httpClientFactory.CreateClient();
        _cache = cache;

        // Configure HTTP client
        _httpClient.Timeout = _config.Cas.Timeout;
        if (!string.IsNullOrEmpty(_config.Cas.UserAgent))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", _config.Cas.UserAgent);
        }
    }

    /// <summary>
    /// Check if CAS check should be executed
    /// </summary>
    public bool ShouldExecute(SpamCheckRequest request)
    {
        // Check if CAS check is enabled
        if (!_config.Cas.Enabled)
        {
            return false;
        }

        // Need user ID for CAS lookup
        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Execute CAS spam check
    /// </summary>
    public async Task<SpamCheckResponse> CheckAsync(SpamCheckRequest request, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"cas_check_{request.UserId}";

        // Check cache first
        if (_cache.TryGetValue(cacheKey, out CasResponse? cachedResponse) && cachedResponse != null)
        {
            _logger.LogDebug("CAS check for user {UserId}: Using cached result", request.UserId);
            return CreateResponse(cachedResponse, fromCache: true);
        }

        try
        {
            // Make API request to CAS
            var apiUrl = $"{_config.Cas.ApiUrl.TrimEnd('/')}/check?user_id={request.UserId}";
            _logger.LogDebug("CAS check for user {UserId}: Calling {ApiUrl}", request.UserId, apiUrl);

            using var response = await _httpClient.GetAsync(apiUrl, cancellationToken);

            // Fail open on any error
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("CAS API returned {StatusCode} for user {UserId}", response.StatusCode, request.UserId);
                return CreateFailResponse("CAS API error");
            }

            var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var casResponse = JsonSerializer.Deserialize<CasResponse>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (casResponse == null)
            {
                _logger.LogWarning("Failed to parse CAS API response for user {UserId}", request.UserId);
                return CreateFailResponse("Failed to parse CAS response");
            }

            // Cache the result for 1 hour
            _cache.Set(cacheKey, casResponse, TimeSpan.FromHours(1));

            return CreateResponse(casResponse, fromCache: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CAS check failed for user {UserId}", request.UserId);
            return CreateFailResponse("CAS check failed due to error", ex);
        }
    }

    /// <summary>
    /// Create spam check response from CAS API response
    /// </summary>
    private SpamCheckResponse CreateResponse(CasResponse casResponse, bool fromCache)
    {
        var isSpam = casResponse.Ok && casResponse.Result?.IsBanned == true;
        var confidence = isSpam ? 95 : 0; // High confidence if CAS says user is banned

        var details = isSpam
            ? $"User banned in CAS database{(fromCache ? " (cached)" : "")}"
            : $"User not found in CAS database{(fromCache ? " (cached)" : "")}";

        _logger.LogDebug("CAS check completed: IsSpam={IsSpam}, Confidence={Confidence}, FromCache={FromCache}",
            isSpam, confidence, fromCache);

        return new SpamCheckResponse
        {
            CheckName = CheckName,
            IsSpam = isSpam,
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
            IsSpam = false, // Fail open
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