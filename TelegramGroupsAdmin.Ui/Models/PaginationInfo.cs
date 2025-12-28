namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Pagination metadata for paginated responses.
/// </summary>
public record PaginationInfo(
    int Page,
    int PageSize,
    int TotalCount,
    bool HasMore
);
