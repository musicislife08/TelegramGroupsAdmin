namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Result of a DM delivery attempt
/// </summary>
public record DmDeliveryResult
{
    /// <summary>
    /// True if the DM was delivered successfully
    /// </summary>
    public bool DmSent { get; init; }

    /// <summary>
    /// True if DM failed and message was posted in chat as fallback
    /// </summary>
    public bool FallbackUsed { get; init; }

    /// <summary>
    /// True if the delivery failed completely (no DM, no fallback, or fallback failed)
    /// </summary>
    public bool Failed { get; init; }

    /// <summary>
    /// Optional error message if delivery failed
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Message ID of the fallback message in chat (if fallback was used)
    /// </summary>
    public int? FallbackMessageId { get; init; }

    /// <summary>
    /// Message ID of the successfully sent DM (if DM was sent)
    /// </summary>
    public int? MessageId { get; init; }
}
