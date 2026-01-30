using System.Text.Json;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Configuration.Models.ContentDetection;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for checking users against CAS (Combot Anti-Spam) database.
/// Moved from ContentDetection pipeline - CAS checks USER status, not message content.
/// Should run on user join to auto-ban known spammers before they can post.
/// </summary>
public class CasCheckService : ICasCheckService
{
    private readonly ILogger<CasCheckService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly HybridCache _cache;

    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);
    private static readonly HybridCacheEntryOptions CacheOptions = new() { Expiration = CacheDuration };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public CasCheckService(
        ILogger<CasCheckService> logger,
        IServiceProvider serviceProvider,
        IHttpClientFactory httpClientFactory,
        HybridCache cache)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _httpClientFactory = httpClientFactory;
        _cache = cache;
    }

    public async Task<CasCheckResult> CheckUserAsync(long userId, CancellationToken cancellationToken = default)
    {
        // Load CAS config (global config with chat_id=0)
        using var scope = _serviceProvider.CreateScope();
        var configService = scope.ServiceProvider.GetRequiredService<IConfigService>();
        var detectionConfig = await configService.GetEffectiveAsync<ContentDetectionConfig>(ConfigType.ContentDetection, 0);
        var casConfig = detectionConfig?.Cas ?? new CasConfig();

        if (!casConfig.Enabled)
        {
            _logger.LogDebug("CAS check disabled, skipping user {UserId}", userId);
            return new CasCheckResult(false, null);
        }

        // Use GetOrCreateAsync for cache-aside with stampede protection
        // Factory throws on API failure to prevent caching transient errors
        var cacheKey = $"cas_user_{userId}";
        try
        {
            return await _cache.GetOrCreateAsync(
                cacheKey,
                async ct => await CallCasApiAsync(userId, casConfig, ct),
                CacheOptions,
                cancellationToken: cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("CAS API"))
        {
            // API failure - fail open (don't cache, retry next time)
            _logger.LogWarning(ex, "CAS check failed for user {UserId}, failing open", userId);
            return new CasCheckResult(false, null);
        }
    }

    /// <summary>
    /// Call the CAS API to check if a user is banned.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown on API failure (non-success status, unparseable response, timeout, network error).
    /// Prevents HybridCache from caching transient errors.
    /// </exception>
    private async Task<CasCheckResult> CallCasApiAsync(long userId, CasConfig casConfig, CancellationToken cancellationToken)
    {
        try
        {
            var httpClient = _httpClientFactory.CreateClient();
            var apiUrl = $"{casConfig.ApiUrl.TrimEnd('/')}/check?user_id={userId}";

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(casConfig.Timeout);

            using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            if (!string.IsNullOrEmpty(casConfig.UserAgent))
            {
                request.Headers.Add("User-Agent", casConfig.UserAgent);
            }

            _logger.LogDebug("CAS check for user {UserId}: Calling {ApiUrl}", userId, apiUrl);

            using var response = await httpClient.SendAsync(request, timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"CAS API returned {response.StatusCode}");
            }

            var jsonContent = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            var casResponse = JsonSerializer.Deserialize<CasApiResponse>(jsonContent, JsonOptions);

            if (casResponse == null)
            {
                throw new InvalidOperationException("CAS API returned unparseable response");
            }

            // CAS API: ok=true means user IS in the ban database (banned)
            // ok=false means user is NOT in the database (not banned)
            var isBanned = casResponse.Ok;
            var reason = isBanned && casResponse.Result != null
                ? $"CAS ban ({casResponse.Result.Offenses} offense(s))"
                : null;

            var result = new CasCheckResult(isBanned, reason);

            if (isBanned)
            {
                _logger.LogInformation("CAS check: User {UserId} is BANNED (reason: {Reason})",
                    userId, reason ?? "No reason provided");
            }
            else
            {
                _logger.LogDebug("CAS check: User {UserId} not found in database", userId);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException("CAS API timed out");
        }
        catch (InvalidOperationException)
        {
            throw; // Re-throw our own exceptions
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"CAS API error: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// CAS API response format.
    /// ok=true means user is banned, ok=false means not found (not banned).
    /// </summary>
    private record CasApiResponse
    {
        public bool Ok { get; init; }
        public CasResult? Result { get; init; }
        public string? Description { get; init; }
    }

    /// <summary>
    /// CAS ban details returned when ok=true.
    /// </summary>
    private record CasResult
    {
        public int Offenses { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("time_added")]
        public DateTimeOffset? TimeAdded { get; init; }
    }
}
