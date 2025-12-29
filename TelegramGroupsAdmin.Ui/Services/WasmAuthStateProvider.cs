using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using TelegramGroupsAdmin.Ui.Api;
using TelegramGroupsAdmin.Ui.Models;

namespace TelegramGroupsAdmin.Ui.Services;

/// <summary>
/// Authentication state provider for Blazor WebAssembly.
/// Calls GET /api/auth/me to determine if user is authenticated.
/// Implements IDisposable to properly clean up the SemaphoreSlim.
/// </summary>
public class WasmAuthStateProvider : AuthenticationStateProvider, IDisposable
{
    private static readonly JsonSerializerOptions s_jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IHttpClientFactory _httpClientFactory;

    // Cache the auth state to avoid repeated API calls
    private AuthenticationState? _cachedAuthState;

    // Prevent concurrent API calls during initialization
    private Task<AuthenticationState>? _pendingAuthStateTask;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public WasmAuthStateProvider(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    private HttpClient Http => _httpClientFactory.CreateClient(HttpClientNames.Api);

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        // Return cached state if available
        if (_cachedAuthState != null)
        {
            return _cachedAuthState;
        }

        // Use semaphore to prevent multiple concurrent API calls
        await _semaphore.WaitAsync();
        try
        {
            // Double-check after acquiring lock (another thread may have cached it)
            if (_cachedAuthState != null)
            {
                return _cachedAuthState;
            }

            // If there's already a pending request, wait for it
            if (_pendingAuthStateTask != null)
            {
                return await _pendingAuthStateTask;
            }

            // Start the API call
            _pendingAuthStateTask = FetchAuthStateAsync();
            var result = await _pendingAuthStateTask;
            _pendingAuthStateTask = null;

            return result;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task<AuthenticationState> FetchAuthStateAsync()
    {
        try
        {
            // Call the /api/auth/me endpoint to get current user info
            // Returns 200 with user data if authenticated, 200 with empty body if not
            var response = await Http.GetAsync(Routes.Auth.Me);

            if (response.IsSuccessStatusCode)
            {
                // Check if response has content (empty = not authenticated)
                var content = await response.Content.ReadAsStringAsync();
                if (!string.IsNullOrWhiteSpace(content))
                {
                    var userInfo = JsonSerializer.Deserialize<AuthMeResponse>(content, s_jsonOptions);

                    if (userInfo != null)
                    {
                        var claims = new List<Claim>
                        {
                            new(ClaimTypes.NameIdentifier, userInfo.UserId),
                            new(ClaimTypes.Name, userInfo.Email),
                            new(ClaimTypes.Email, userInfo.Email),
                            new("permission_level", userInfo.PermissionLevel.ToString())
                        };

                        var identity = new ClaimsIdentity(claims, "cookie");
                        var principal = new ClaimsPrincipal(identity);

                        _cachedAuthState = new AuthenticationState(principal);
                        return _cachedAuthState;
                    }
                }
            }
        }
        catch (HttpRequestException)
        {
            // API not available or network error - treat as unauthenticated
        }

        // Not authenticated - cache this state too to prevent repeated API calls
        _cachedAuthState = new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        return _cachedAuthState;
    }

    /// <summary>
    /// Call after successful login to refresh auth state.
    /// </summary>
    public void NotifyAuthenticationStateChanged()
    {
        _cachedAuthState = null;
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    /// <summary>
    /// Call after logout to clear auth state.
    /// </summary>
    public void NotifyUserLoggedOut()
    {
        _cachedAuthState = new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        NotifyAuthenticationStateChanged(Task.FromResult(_cachedAuthState));
    }

    /// <summary>
    /// Disposes the SemaphoreSlim used for concurrent auth state checks.
    /// </summary>
    public void Dispose()
    {
        _semaphore.Dispose();
        GC.SuppressFinalize(this);
    }
}
