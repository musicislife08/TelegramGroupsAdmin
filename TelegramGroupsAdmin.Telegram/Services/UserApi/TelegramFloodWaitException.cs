namespace TelegramGroupsAdmin.Telegram.Services.UserApi;

/// <summary>
/// Thrown when the Telegram API rate limit (FLOOD_WAIT) is active and the wait
/// exceeds the client's transparent retry threshold. Callers should treat this
/// as a temporary failure — do NOT permanently exclude the user from rescans.
/// </summary>
public sealed class TelegramFloodWaitException(int waitSeconds, DateTimeOffset gateExpiresAt)
    : Exception($"Telegram rate limited — FLOOD_WAIT_{waitSeconds} (gate expires {gateExpiresAt:u})")
{
    public int WaitSeconds { get; } = waitSeconds;
    public DateTimeOffset GateExpiresAt { get; } = gateExpiresAt;
}
