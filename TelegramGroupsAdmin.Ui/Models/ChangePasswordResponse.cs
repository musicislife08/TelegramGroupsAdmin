namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Response for password change attempts.
/// </summary>
public record ChangePasswordResponse : IApiResponse
{
    public bool Success { get; init; }
    public string? Error { get; init; }

    public static ChangePasswordResponse Ok() => new() { Success = true };
    public static ChangePasswordResponse Fail(string error) => new() { Success = false, Error = error };
}
