using System.Security.Cryptography;
using System.Text;

namespace TelegramGroupsAdmin.Core.Utilities;

/// <summary>
/// Hashing utilities shared across all domain libraries
/// </summary>
public static class HashUtilities
{
    /// <summary>
    /// Compute SHA256 hash of file stream
    /// Used for file deduplication and integrity verification
    /// </summary>
    /// <param name="stream">File stream to hash</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Lowercase hex string representation of SHA256 hash</returns>
    public static async Task<string> ComputeSHA256Async(Stream stream, CancellationToken cancellationToken = default)
    {
        var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Compute SHA256 hash of string content
    /// Used for content deduplication and correlation
    /// </summary>
    /// <param name="content">String content to hash</param>
    /// <returns>Uppercase hex string representation of SHA256 hash</returns>
    public static string ComputeSHA256(string content)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(hashBytes);
    }

    /// <summary>
    /// Compute SHA256 hash of message content (text + URLs) for duplicate detection
    /// Normalizes content to lowercase and trims whitespace before hashing
    /// </summary>
    /// <param name="messageText">Message text content</param>
    /// <param name="urls">URLs extracted from message (typically JSON-serialized)</param>
    /// <returns>Uppercase hex string representation of SHA256 hash</returns>
    public static string ComputeContentHash(string messageText, string urls)
    {
        var normalized = string.Concat(
            messageText.ToLowerInvariant().Trim(),
            urls.ToLowerInvariant().Trim());
        return ComputeSHA256(normalized);
    }
}
