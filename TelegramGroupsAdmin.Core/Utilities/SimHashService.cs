using System.IO.Hashing;
using System.Numerics;
using System.Text;

namespace TelegramGroupsAdmin.Core.Utilities;

/// <summary>
/// Computes 64-bit SimHash fingerprints for text similarity detection.
/// Similar texts produce hashes with low Hamming distance (few differing bits).
/// Used for O(1) training data deduplication instead of O(n) Jaccard comparison.
///
/// Algorithm:
/// 1. Tokenize text using shared TextTokenizer (lowercase, filter single chars)
/// 2. For each token, compute a 64-bit hash
/// 3. For each bit position 0-63: increment counter if bit set, decrement if not
/// 4. Final hash: bit i = 1 if counter[i] > 0, else 0
///
/// Similar texts → Similar hashes → Low Hamming distance
/// </summary>
public class SimHashService
{
    /// <summary>
    /// Default maximum Hamming distance for similarity detection.
    /// 10 bits difference out of 64 ≈ 84% similarity threshold.
    /// Aligns with the 90% Jaccard threshold used in the original deduplication approach.
    /// </summary>
    public const int DefaultMaxDistance = HashingConstants.SimHashDefaultMaxDistance;

    /// <summary>
    /// Compute 64-bit SimHash fingerprint for text.
    /// Returns 0 for null, empty, or whitespace-only text.
    /// </summary>
    public long ComputeHash(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        var tokens = TextTokenizer.TokenizeToSet(text);
        if (tokens.Count == 0)
            return 0;

        // Bit counters for 64 positions
        Span<int> counters = stackalloc int[HashingConstants.SimHashBitCount];

        foreach (var token in tokens)
        {
            var tokenHash = ComputeTokenHash(token);
            for (int i = 0; i < HashingConstants.SimHashBitCount; i++)
            {
                if ((tokenHash & (1L << i)) != 0)
                    counters[i]++;
                else
                    counters[i]--;
            }
        }

        // Build final hash from counter signs
        long result = 0;
        for (int i = 0; i < HashingConstants.SimHashBitCount; i++)
        {
            if (counters[i] > 0)
                result |= (1L << i);
        }
        return result;
    }

    /// <summary>
    /// Calculate Hamming distance (number of differing bits) between two hashes.
    /// Lower distance = more similar texts.
    /// Range: 0 (identical) to 64 (completely different).
    /// </summary>
    public int HammingDistance(long hash1, long hash2)
    {
        return BitOperations.PopCount((ulong)(hash1 ^ hash2));
    }

    /// <summary>
    /// Check if two hashes are similar (within threshold).
    /// Default threshold of 10 bits ≈ 84% similarity.
    /// </summary>
    /// <param name="hash1">First hash</param>
    /// <param name="hash2">Second hash</param>
    /// <param name="maxDistance">Maximum Hamming distance to consider similar (default: DefaultMaxDistance)</param>
    public bool AreSimilar(long hash1, long hash2, int maxDistance = DefaultMaxDistance)
    {
        return HammingDistance(hash1, hash2) <= maxDistance;
    }

    /// <summary>
    /// Compute a 64-bit hash for a single token using XxHash3.
    /// XxHash3 is deterministic across process restarts (unlike string.GetHashCode()),
    /// which is required since we persist hashes to the database.
    /// </summary>
    private static long ComputeTokenHash(string token)
    {
        // XxHash3 is deterministic and optimized for small inputs like tokens
        var bytes = Encoding.UTF8.GetBytes(token);
        return (long)XxHash3.HashToUInt64(bytes);
    }
}
