using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Imaging;
using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.Core.Services;

/// <summary>
/// Computes and compares perceptual hashes (average hash) of images.
///
/// The hash deliberately depends on no imaging library beyond a decode: the
/// downsample is a box average performed here, so the stored hash values survive
/// a change of imaging library or codec. Changing anything in
/// <see cref="ComputePhotoHashAsync"/> invalidates every persisted photo_hash.
/// </summary>
public class PhotoHashService : IPhotoHashService
{
    private readonly IImageProcessor _imageProcessor;
    private readonly ILogger<PhotoHashService> _logger;
    private const int HashSize = HashingConstants.PhotoHashSize;

    public PhotoHashService(IImageProcessor imageProcessor, ILogger<PhotoHashService> logger)
    {
        _imageProcessor = imageProcessor;
        _logger = logger;
    }

    /// <summary>
    /// Average hash (aHash):
    /// 1. Reduce to an 8x8 grid of Rec.601 luminance by box-averaging the source
    /// 2. Compute the mean of the 64 cells
    /// 3. Emit one bit per cell: 1 if above the mean, 0 otherwise
    /// </summary>
    public async Task<byte[]?> ComputePhotoHashAsync(string photoPath)
    {
        try
        {
            if (!File.Exists(photoPath))
            {
                _logger.LogTrace("Photo file not found: {PhotoPath}", photoPath);
                return null;
            }

            await using var stream = File.OpenRead(photoPath);
            var cells = _imageProcessor.ReadLuminanceGrid(stream, HashSize);
            if (cells is null)
            {
                _logger.LogDebug("Could not decode image for hashing: {PhotoPath}", photoPath);
                return null;
            }

            long sum = 0;
            foreach (var cell in cells) sum += cell;
            var average = sum / (HashSize * HashSize);

            var hash = new byte[HashingConstants.PhotoHashByteCount];
            for (var i = 0; i < HashingConstants.PhotoHashBitCount; i++)
            {
                if (cells[i] > average)
                {
                    hash[i / HashingConstants.BitsPerByte] |= (byte)(1 << (i % HashingConstants.BitsPerByte));
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
    /// Compares two hashes by Hamming distance, as a similarity from 0.0 to 1.0.
    /// 1.0 is identical; 0.0 is maximally different.
    /// </summary>
    public double CompareHashes(byte[] hash1, byte[] hash2)
    {
        if (hash1.Length != HashingConstants.PhotoHashByteCount || hash2.Length != HashingConstants.PhotoHashByteCount)
        {
            throw new ArgumentException($"Photo hashes must be exactly {HashingConstants.PhotoHashByteCount} bytes ({HashingConstants.PhotoHashBitCount} bits)");
        }

        var hammingDistance = BitwiseUtilities.HammingDistance(hash1, hash2);
        return 1.0 - (hammingDistance / (double)HashingConstants.PhotoHashBitCount);
    }
}
