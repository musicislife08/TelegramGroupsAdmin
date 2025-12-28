namespace TelegramGroupsAdmin.Ui.Server.Services;

public record InviteListItem(
    string Token,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string? UsedBy,
    DateTimeOffset? UsedAt,
    bool IsExpired,
    bool IsUsed
);
