namespace TelegramGroupsAdmin.Services;

public record InviteResult(
    string Token,
    string Url,
    DateTimeOffset ExpiresAt
);
