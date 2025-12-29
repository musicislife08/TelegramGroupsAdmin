using Microsoft.AspNetCore.Components;
using TelegramGroupsAdmin.Ui.Navigation;

namespace TelegramGroupsAdmin.Ui.Services;

/// <summary>
/// DelegatingHandler that intercepts HTTP responses and handles global error cases.
/// - 401 Unauthorized: Redirects to login
/// - 403 Forbidden: Notifies UI to show error message
/// - 5xx Server Errors: Notifies UI to show error message
/// </summary>
public sealed class HttpErrorInterceptor : DelegatingHandler
{
    private readonly NavigationManager _navigation;
    private readonly HttpErrorNotificationService _errorService;

    public HttpErrorInterceptor(NavigationManager navigation, HttpErrorNotificationService errorService)
    {
        _navigation = navigation;
        _errorService = errorService;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);

        // Only intercept error responses - let success and 400 BadRequest through
        // (400 is handled by component-level ApiResult pattern)
        if (response.IsSuccessStatusCode || (int)response.StatusCode == 400)
        {
            return response;
        }

        var statusCode = (int)response.StatusCode;

        switch (statusCode)
        {
            case 401:
                // Session expired or not authenticated - redirect to login
                // Use forceLoad to ensure clean state
                _navigation.NavigateTo(PageRoutes.Auth.Login, forceLoad: true);
                break;

            case 403:
                // Forbidden - user authenticated but lacks permission
                _errorService.NotifyError(HttpErrorEvent.Forbidden());
                break;

            case 404:
                // Not found - requested resource doesn't exist
                _errorService.NotifyError(HttpErrorEvent.NotFound());
                break;

            case >= 500:
                // Server error - notify UI
                _errorService.NotifyError(HttpErrorEvent.ServerError());
                break;
        }

        return response;
    }
}
