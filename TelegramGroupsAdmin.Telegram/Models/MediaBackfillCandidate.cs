namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// A curated message whose photo or video has no media features yet (startup media-feature backfill).
/// Paths are as stored: <see cref="PhotoLocalPath"/> is relative to the media root, <see cref="MediaLocalPath"/> is a bare
/// file name under the <see cref="MediaType"/> subdirectory (<see cref="MediaType.Photo"/> for a photo message).
/// </summary>
public sealed record MediaBackfillCandidate(int MessageId, long ChatId, string? PhotoLocalPath, string? MediaLocalPath, MediaType? MediaType);
