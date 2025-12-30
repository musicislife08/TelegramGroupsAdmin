namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Response for unlinking a Telegram account.
/// </summary>
public record UnlinkTelegramResponse : IApiResponse
{
    public bool Success { get; init; }
    public string? Error { get; init; }

    public static UnlinkTelegramResponse Ok() => new() { Success = true };
    public static UnlinkTelegramResponse Fail(string error) => new() { Success = false, Error = error };
}
