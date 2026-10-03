namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// A user's names as seen at one point in time. <see cref="ObservedAt"/> is when Telegram
/// reported them (message date, edit date, or scan fetch time), not when we processed them,
/// so an older observation processed late never overwrites newer names.
/// </summary>
public sealed record ObservedUser(
    long Id,
    string? FirstName,
    string? LastName,
    string? Username,
    bool IsBot,
    ObservationSource Source,
    DateTimeOffset ObservedAt);
