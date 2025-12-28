namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Standard API response types for consistent client-side handling.
/// Shared between WASM client (deserialization) and Server (serialization).
/// All action endpoints return one of these types.
/// </summary>

/// <summary>
/// Base response for simple success/failure operations.
/// </summary>
public record ApiResponse(bool Success, string? Error = null);

/// <summary>
/// Response for message action operations (delete, spam, ham, etc.).
/// </summary>
public record MessageActionResponse(
    bool Success,
    string? Error = null,
    bool? MessageDeleted = null,
    int? ChatsAffected = null,
    bool? TrustRestored = null
) : ApiResponse(Success, Error);

/// <summary>
/// Response for temporary ban operations.
/// </summary>
public record TempBanResponse(
    bool Success,
    string? Error = null,
    DateTimeOffset? BannedUntil = null
) : ApiResponse(Success, Error);

/// <summary>
/// Response for send message operations.
/// </summary>
public record SendMessageResponse(
    bool Success,
    string? Error = null,
    long? MessageId = null
) : ApiResponse(Success, Error);
