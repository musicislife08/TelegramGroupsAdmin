namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Base response for simple success/failure operations.
/// Use static factory methods for clarity: ApiResponse.Ok() or ApiResponse.Fail("error")
/// </summary>
public record ApiResponse : IApiResponse
{
    public bool Success { get; init; }
    public string? Error { get; init; }

    public static ApiResponse Ok() => new() { Success = true };
    public static ApiResponse Fail(string error) => new() { Success = false, Error = error };
}
