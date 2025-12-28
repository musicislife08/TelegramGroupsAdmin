namespace TelegramGroupsAdmin.Ui.Server.Constants;

/// <summary>
/// Constants for Blazor Server and SignalR configuration.
/// </summary>
public static class BlazorConstants
{
    /// <summary>
    /// Maximum SignalR message size for Blazor Server (20 MB).
    /// Allows for base64-encoded image paste with overhead.
    /// </summary>
    public const int MaxSignalRMessageSize = 20 * 1024 * 1024;
}
