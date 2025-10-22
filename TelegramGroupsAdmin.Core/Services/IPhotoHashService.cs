namespace TelegramGroupsAdmin.Core.Services;

/// <summary>
/// Service for computing and comparing perceptual hashes (pHash) of images
/// Used for detecting impersonation via stolen profile photos, duplicate images, visual similarity
/// </summary>
public interface IPhotoHashService
{
    /// <summary>
    /// Computes a perceptual hash (pHash) of an image file
    /// pHash is robust to minor modifications (cropping, compression, filters)
    /// </summary>
    /// <param name="photoPath">Absolute path to the image file</param>
    /// <returns>64-bit hash as byte array (8 bytes), or null if file doesn't exist/invalid</returns>
    Task<byte[]?> ComputePhotoHashAsync(string photoPath);

    /// <summary>
    /// Compares two photo hashes and returns similarity score (0.0-1.0)
    /// Uses Hamming distance to measure bit differences
    /// </summary>
    /// <param name="hash1">First photo hash</param>
    /// <param name="hash2">Second photo hash</param>
    /// <returns>Similarity score: 1.0 = identical, 0.0 = completely different</returns>
    double CompareHashes(byte[] hash1, byte[] hash2);
}
