namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Response for temporary ban operations.
/// </summary>
public record TempBanResponse : IApiResponse
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public DateTimeOffset? BannedUntil { get; init; }

    public static TempBanResponse Ok(DateTimeOffset? bannedUntil = null) => new()
    {
        Success = true,
        BannedUntil = bannedUntil
    };

    public static TempBanResponse Fail(string error) => new() { Success = false, Error = error };
}
