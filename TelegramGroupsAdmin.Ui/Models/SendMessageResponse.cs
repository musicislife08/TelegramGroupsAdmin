namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Response for send message operations.
/// </summary>
public record SendMessageResponse(
    bool Success,
    string? Error = null,
    long? MessageId = null
) : ApiResponse(Success, Error);
