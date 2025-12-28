namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Interface for all API response types to enable shared extension methods.
/// </summary>
public interface IApiResponse
{
    bool Success { get; }
    string? Error { get; }
}
