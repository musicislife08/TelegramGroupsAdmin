namespace TelegramGroupsAdmin.Ui.Server.Services;

public record InviteResult(
    string Token,
    string Url,
    DateTimeOffset ExpiresAt
);
