namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Request for CAS (Combot Anti-Spam) check
/// </summary>
public sealed class CasCheckRequest : ContentCheckRequestBase
{
    public required string ApiUrl { get; init; }
    public required TimeSpan Timeout { get; init; }
    public required string? UserAgent { get; init; }
}
