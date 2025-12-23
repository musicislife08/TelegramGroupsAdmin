namespace TelegramGroupsAdmin.Core;

/// <summary>
/// Centralized constants for hashing algorithms and similarity detection.
/// </summary>
public static class HashingConstants
{
    /// <summary>
    /// Size of perceptual hash (aHash) grid in pixels (8x8 = 64-bit hash).
    /// Used for image similarity detection via PhotoHashService.
    /// </summary>
    public const int PhotoHashSize = 8;

    /// <summary>
    /// Total number of bits in photo hash (64 bits = 8 bytes).
    /// Photo hashes are 8x8 pixel grids reduced to 64 bits for Hamming distance comparison.
    /// </summary>
    public const int PhotoHashBitCount = 64;

    /// <summary>
    /// Expected byte array size for photo hashes (8 bytes = 64 bits).
    /// Photo hash validation requires exactly 8 bytes.
    /// </summary>
    public const int PhotoHashByteCount = 8;

    /// <summary>
    /// Number of bits in SimHash fingerprints (64 bits).
    /// SimHash produces 64-bit fingerprints for text similarity detection.
    /// </summary>
    public const int SimHashBitCount = 64;

    /// <summary>
    /// Default maximum Hamming distance for SimHash similarity detection.
    /// 10 bits difference out of 64 ≈ 84% similarity threshold.
    /// Aligns with the 90% Jaccard threshold used in deduplication.
    /// </summary>
    public const int SimHashDefaultMaxDistance = 10;
}
