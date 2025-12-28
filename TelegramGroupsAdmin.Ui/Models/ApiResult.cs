namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Represents the result of an API call, encapsulating either a successful response or an error.
/// </summary>
/// <typeparam name="T">The response type implementing IApiResponse</typeparam>
public sealed class ApiResult<T> where T : class, IApiResponse
{
    public bool IsSuccess { get; private init; }
    public T? Value { get; private init; }
    public string? Error { get; private init; }

    private ApiResult() { }

    public static ApiResult<T> Success(T value) => new()
    {
        IsSuccess = true,
        Value = value
    };

    public static ApiResult<T> Failure(string error) => new()
    {
        IsSuccess = false,
        Error = error
    };
}
