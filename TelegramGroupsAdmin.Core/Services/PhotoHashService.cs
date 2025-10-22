using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.Core.Services;

/// <summary>
/// Service for computing and comparing perceptual hashes (pHash) of images
/// Uses average hash (aHash) algorithm which is robust to minor modifications
/// </summary>
public class PhotoHashService : IPhotoHashService
{
    private readonly ILogger<PhotoHashService> _logger;
    private const int HashSize = 8; // 8x8 = 64-bit hash

    public PhotoHashService(ILogger<PhotoHashService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Computes a perceptual hash using average hash (aHash) algorithm:
    /// 1. Resize image to 8x8 pixels
    /// 2. Convert to grayscale
    /// 3. Compute average pixel brightness
    /// 4. Create 64-bit hash: 1 if pixel > average, 0 otherwise
    /// </summary>
    public async Task<byte[]?> ComputePhotoHashAsync(string photoPath)
    {
        try
        {
            if (!File.Exists(photoPath))
            {
                _logger.LogWarning("Photo file not found: {PhotoPath}", photoPath);
                return null;
            }

            await using var stream = File.OpenRead(photoPath);
            using var image = await Image.LoadAsync<L8>(stream); // Load as grayscale directly

            // Resize to 8x8 for hash computation
            image.Mutate(x => x.Resize(HashSize, HashSize));

            // Compute average pixel value
            long sum = 0;
            var pixels = new byte[HashSize * HashSize];

            // Access pixels using indexer (ImageSharp v3 API)
            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < HashSize; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < HashSize; x++)
                    {
                        var pixelValue = row[x].PackedValue;
                        pixels[y * HashSize + x] = pixelValue;
                        sum += pixelValue;
                    }
                }
            });

            var average = sum / (HashSize * HashSize);

            // Create 64-bit hash (8 bytes)
            var hash = new byte[8];
            for (int i = 0; i < 64; i++)
            {
                if (pixels[i] > average)
                {
                    // Set bit i in the hash
                    hash[i / 8] |= (byte)(1 << (i % 8));
                }
            }

            _logger.LogDebug("Computed photo hash for {PhotoPath}: {Hash}", photoPath, Convert.ToHexString(hash));
            return hash;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compute photo hash for {PhotoPath}", photoPath);
            return null;
        }
    }

    /// <summary>
    /// Compares two hashes using Hamming distance
    /// Returns similarity as a value from 0.0 to 1.0
    /// 1.0 = identical (Hamming distance 0)
    /// 0.0 = completely different (Hamming distance 64)
    /// </summary>
    public double CompareHashes(byte[] hash1, byte[] hash2)
    {
        if (hash1.Length != 8 || hash2.Length != 8)
        {
            throw new ArgumentException("Photo hashes must be exactly 8 bytes (64 bits)");
        }

        var hammingDistance = BitwiseUtilities.HammingDistance(hash1, hash2);

        // Convert Hamming distance to similarity score
        // Distance 0 = 100% similar, Distance 64 = 0% similar
        var similarity = 1.0 - (hammingDistance / 64.0);

        return similarity;
    }
}
