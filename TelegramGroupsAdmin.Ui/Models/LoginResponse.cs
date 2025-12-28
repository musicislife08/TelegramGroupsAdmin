namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Response for login attempts. Use static factory methods for clarity.
/// </summary>
public record LoginResponse : IApiResponse
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public bool RequiresTotp { get; init; }
    public bool RequiresTotpSetup { get; init; }
    public string? UserId { get; init; }
    public string? IntermediateToken { get; init; }

    public static LoginResponse Ok() => new() { Success = true };

    public static LoginResponse Fail(string error) => new() { Success = false, Error = error };

    public static LoginResponse MustVerifyTotp(string userId, string intermediateToken) => new()
    {
        Success = true,
        RequiresTotp = true,
        UserId = userId,
        IntermediateToken = intermediateToken
    };

    public static LoginResponse MustSetupTotp(string userId, string intermediateToken) => new()
    {
        Success = true,
        RequiresTotpSetup = true,
        UserId = userId,
        IntermediateToken = intermediateToken
    };
}
