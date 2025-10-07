namespace TelegramGroupsAdmin;

public record FetchResult<T>(bool Success, T? Value, string? ErrorMessage)
{
    public static FetchResult<T> Ok(T value) => new(true, value, null);
    public static FetchResult<T> Fail(string error) => new(false, default, error);
}