namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// DTO for linked Telegram account display in Profile page.
/// </summary>
public record LinkedTelegramAccountDto(
    long Id,
    long TelegramId,
    string? TelegramUsername,
    DateTimeOffset LinkedAt
);
