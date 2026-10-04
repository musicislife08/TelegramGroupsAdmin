namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Result of recording an observation: the user as stored afterwards, and the previous names
/// when this observation changed them (null when nothing changed or the observation was stale).
/// </summary>
public sealed record ObservedNamesResult(TelegramUser User, PreviousNames? Renamed);
