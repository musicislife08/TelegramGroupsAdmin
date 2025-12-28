namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Aggregate response for the Messages page initial load.
/// Bundles all data needed to render the page in a single HTTP call.
/// </summary>
public record MessagesPageResponse(
    List<ChatSummary> Chats,
    List<MessageWithMetadata> Messages,
    PaginationInfo Pagination,
    long? SelectedChatId,
    MessagesPageUserContext UserContext
);
