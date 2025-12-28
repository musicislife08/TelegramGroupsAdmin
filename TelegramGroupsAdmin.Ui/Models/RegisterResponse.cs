namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Response for registration attempts. Use static factory methods for clarity.
/// </summary>
public record RegisterResponse : IApiResponse
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? Message { get; init; }

    public static RegisterResponse Ok(string message) => new() { Success = true, Message = message };
    public static RegisterResponse Fail(string error) => new() { Success = false, Error = error };
}
