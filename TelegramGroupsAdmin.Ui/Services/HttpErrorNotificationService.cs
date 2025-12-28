namespace TelegramGroupsAdmin.Ui.Services;

/// <summary>
/// Service for broadcasting HTTP error events from DelegatingHandler to UI components.
/// In WASM, scoped services are effectively singletons per browser tab.
/// </summary>
public sealed class HttpErrorNotificationService
{
    public event Action<HttpErrorEvent>? OnError;

    public void NotifyError(HttpErrorEvent error)
    {
        OnError?.Invoke(error);
    }
}

public record HttpErrorEvent(int StatusCode, string Message)
{
    public static HttpErrorEvent Unauthorized() => new(401, "Your session has expired. Please log in again.");
    public static HttpErrorEvent Forbidden() => new(403, "You don't have permission to perform this action.");
    public static HttpErrorEvent ServerError() => new(500, "An unexpected server error occurred. Please try again later.");
}
