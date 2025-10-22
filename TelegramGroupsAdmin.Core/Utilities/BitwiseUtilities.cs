namespace TelegramGroupsAdmin.Core.Utilities;

/// <summary>
/// Bitwise operation utilities shared across all domain libraries
/// Used for image hashing, checksums, data integrity verification
/// </summary>
public static class BitwiseUtilities
{
    /// <summary>
    /// Calculates Hamming distance (number of differing bits) between two byte arrays
    /// Used in perceptual image hashing for similarity detection
    /// </summary>
    /// <param name="hash1">First byte array</param>
    /// <param name="hash2">Second byte array (must be same length as hash1)</param>
    /// <returns>Number of differing bits between the two arrays</returns>
    public static int HammingDistance(byte[] hash1, byte[] hash2)
    {
        if (hash1 == null || hash2 == null)
            throw new ArgumentNullException(hash1 == null ? nameof(hash1) : nameof(hash2));

        if (hash1.Length != hash2.Length)
            throw new ArgumentException("Byte arrays must be the same length", nameof(hash2));

        int distance = 0;
        for (int i = 0; i < hash1.Length; i++)
        {
            // XOR gives 1 for differing bits, 0 for matching bits
            var xor = hash1[i] ^ hash2[i];

            // Count number of 1 bits (population count)
            distance += PopCount(xor);
        }
        return distance;
    }

    /// <summary>
    /// Counts the number of set bits in a value (population count)
    /// Uses Brian Kernighan's algorithm for efficient bit counting
    /// </summary>
    /// <param name="value">Integer value to count bits in</param>
    /// <returns>Number of bits set to 1 in the value</returns>
    public static int PopCount(int value)
    {
        int count = 0;
        while (value != 0)
        {
            value &= (value - 1); // Clear the lowest set bit
            count++;
        }
        return count;
    }
}
