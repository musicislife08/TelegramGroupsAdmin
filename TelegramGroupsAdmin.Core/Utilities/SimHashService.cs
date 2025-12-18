using System.IO.Hashing;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace TelegramGroupsAdmin.Core.Utilities;

/// <summary>
/// Computes 64-bit SimHash fingerprints for text similarity detection.
/// Similar texts produce hashes with low Hamming distance (few differing bits).
/// Used for O(1) training data deduplication instead of O(n) Jaccard comparison.
///
/// Algorithm:
/// 1. Tokenize text (lowercase, filter single chars)
/// 2. For each token, compute a 64-bit hash
/// 3. For each bit position 0-63: increment counter if bit set, decrement if not
/// 4. Final hash: bit i = 1 if counter[i] > 0, else 0
///
/// Similar texts → Similar hashes → Low Hamming distance
/// </summary>
public partial class SimHashService
{
    [GeneratedRegex(@"[\s\p{P}]+", RegexOptions.Compiled)]
    private static partial Regex TokenSplitter();

    /// <summary>
    /// Compute 64-bit SimHash fingerprint for text.
    /// Returns 0 for null, empty, or whitespace-only text.
    /// </summary>
    public long ComputeHash(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        var tokens = Tokenize(text);
        if (tokens.Count == 0)
            return 0;

        // Bit counters for 64 positions
        Span<int> counters = stackalloc int[64];

        foreach (var token in tokens)
        {
            var tokenHash = ComputeTokenHash(token);
            for (int i = 0; i < 64; i++)
            {
                if ((tokenHash & (1L << i)) != 0)
                    counters[i]++;
                else
                    counters[i]--;
            }
        }

        // Build final hash from counter signs
        long result = 0;
        for (int i = 0; i < 64; i++)
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
    /// <param name="maxDistance">Maximum Hamming distance to consider similar (default: 10)</param>
    public bool AreSimilar(long hash1, long hash2, int maxDistance = 10)
    {
        return HammingDistance(hash1, hash2) <= maxDistance;
    }

    /// <summary>
    /// Tokenize text into unique lowercase tokens, filtering single-character tokens.
    /// Uses same tokenization strategy as TextSimilarityService (Jaccard) for consistency.
    /// </summary>
    private static HashSet<string> Tokenize(string text)
    {
        // ToLowerInvariant handles case folding, so default ordinal comparer is sufficient
        return TokenSplitter()
            .Split(text.ToLowerInvariant())
            .Where(t => t.Length > 1)  // Filter single chars (same as Jaccard)
            .ToHashSet();
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
