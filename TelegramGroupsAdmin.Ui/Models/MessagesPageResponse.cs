namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Aggregate response for the Messages page initial load.
/// Bundles all data needed to render the page in a single HTTP call.
/// Implements IApiResponse for unified error handling.
/// </summary>
public record MessagesPageResponse : IApiResponse
{
    public bool Success { get; init; }
    public string? Error { get; init; }

    public List<ChatSummary>? Chats { get; init; }
    public List<MessageWithMetadata>? Messages { get; init; }
    public PaginationInfo? Pagination { get; init; }
    public long? SelectedChatId { get; init; }
    public MessagesPageUserContext? UserContext { get; init; }

    public static MessagesPageResponse Ok(
        List<ChatSummary> chats,
        List<MessageWithMetadata> messages,
        PaginationInfo pagination,
        long? selectedChatId,
        MessagesPageUserContext userContext) => new()
    {
        Success = true,
        Chats = chats,
        Messages = messages,
        Pagination = pagination,
        SelectedChatId = selectedChatId,
        UserContext = userContext
    };

    public static MessagesPageResponse Fail(string error) => new()
    {
        Success = false,
        Error = error
    };
}
