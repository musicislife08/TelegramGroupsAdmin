namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Results;

/// <summary>
/// Result of a message backfill operation.
/// </summary>
/// <param name="Success">Whether the message now exists in the database.</param>
/// <param name="WasBackfilled">Whether the message was backfilled (vs already existing).</param>
/// <param name="ErrorMessage">Error message if the operation failed.</param>
public record BackfillResult(
    bool Success,
    bool WasBackfilled,
    string? ErrorMessage = null)
{
    /// <summary>
    /// Message already existed in the database.
    /// </summary>
    public static BackfillResult AlreadyExists() =>
        new(true, WasBackfilled: false);

    /// <summary>
    /// Message was successfully backfilled from Telegram.
    /// </summary>
    public static BackfillResult Backfilled() =>
        new(true, WasBackfilled: true);

    /// <summary>
    /// Message does not exist and could not be backfilled.
    /// </summary>
    public static BackfillResult NotFound(string? reason = null) =>
        new(false, WasBackfilled: false, reason ?? "Message not found and could not be backfilled");
}
