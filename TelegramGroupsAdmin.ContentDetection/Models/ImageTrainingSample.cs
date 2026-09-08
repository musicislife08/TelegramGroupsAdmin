namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// A labelled training image reduced to what similarity matching needs.
/// PhotoHash is non-null by construction: rows with a NULL hash are filtered out
/// at the query, because a hash that cannot be compared is not a usable sample.
/// </summary>
public sealed record ImageTrainingSample(byte[] PhotoHash, bool IsSpam);
