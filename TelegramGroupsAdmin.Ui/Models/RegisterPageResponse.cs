namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Aggregate response for the Register page initialization.
/// Implements IApiResponse for unified error handling.
/// </summary>
public record RegisterPageResponse : IApiResponse
{
    public bool Success { get; init; }
    public string? Error { get; init; }

    public bool IsFirstRun { get; init; }
    public bool IsEmailVerificationEnabled { get; init; }

    public static RegisterPageResponse Ok(bool isFirstRun, bool isEmailVerificationEnabled) => new()
    {
        Success = true,
        IsFirstRun = isFirstRun,
        IsEmailVerificationEnabled = isEmailVerificationEnabled
    };

    public static RegisterPageResponse Fail(string error) => new()
    {
        Success = false,
        Error = error
    };
}
