namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// The names a user had before a recorded rename.
/// </summary>
public sealed record PreviousNames(string? FirstName, string? LastName, string? Username);
