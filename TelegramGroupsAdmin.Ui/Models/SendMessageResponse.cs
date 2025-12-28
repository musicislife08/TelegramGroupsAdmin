namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Response for send message operations.
/// </summary>
public record SendMessageResponse : IApiResponse
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public long? MessageId { get; init; }

    public static SendMessageResponse Ok(long? messageId = null) => new()
    {
        Success = true,
        MessageId = messageId
    };

    public static SendMessageResponse Fail(string error) => new() { Success = false, Error = error };
}
