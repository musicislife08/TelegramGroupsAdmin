namespace TelegramGroupsAdmin.Core.Models;

public enum FetchStatus { Success, AlreadyHandled, Error }

/// <summary>
/// Generic result of fetching an entity — success with value, already-handled, or error with message.
/// </summary>
public sealed record FetchResult<T>(FetchStatus Status, T? Value, string? ErrorMessage, Exception? Exception = null)
{
    public bool Success => Status == FetchStatus.Success;
    public static FetchResult<T> Ok(T value) => new(FetchStatus.Success, value, null);
    public static FetchResult<T> Fail(string error) => new(FetchStatus.Error, default, error);
    public static FetchResult<T> Fail(Exception ex) => new(FetchStatus.Error, default, ex.Message, ex);
    public static FetchResult<T> Handled(string message) => new(FetchStatus.AlreadyHandled, default, message);
}
