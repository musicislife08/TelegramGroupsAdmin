namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Result of message cleanup operation including remaining data statistics.
/// The repository returns stats about its domain data as part of the cleanup operation.
/// </summary>
public record MessageCleanupResult(
    int DeletedCount,
    List<string> ImagePaths,
    List<string> MediaPaths,
    int RemainingMessages,
    int RemainingUniqueUsers,
    int RemainingPhotos,
    DateTimeOffset? OldestTimestamp
);
