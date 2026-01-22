namespace TelegramGroupsAdmin.Core;

/// <summary>
/// Centralized constants for security and passphrase generation.
/// </summary>
public static class SecurityConstants
{
    /// <summary>
    /// Minimum number of words required for secure passphrase generation.
    /// EFF Large Wordlist: 5 words = 64.6 bits (minimum acceptable security).
    /// </summary>
    public const int MinimumPassphraseWords = 5;

    /// <summary>
    /// Recommended number of words for passphrase generation.
    /// EFF Large Wordlist: 6 words = 77.5 bits (recommended default).
    /// </summary>
    public const int RecommendedPassphraseWords = 6;

    /// <summary>
    /// Minimum wordlist size required for secure passphrase generation.
    /// Must be at least 5000 words to achieve 77+ bits entropy with 6 words.
    /// </summary>
    public const int MinimumWordlistSize = 5000;

    /// <summary>
    /// Minimum entropy in bits for acceptable passphrase security.
    /// Based on EFF Large Wordlist with 5 words: log2(7776^5) ≈ 64.6 bits.
    /// </summary>
    public const double MinimumEntropyBits = 64.6;

    /// <summary>
    /// Recommended entropy in bits for strong passphrase security.
    /// Based on EFF Large Wordlist with 6 words: log2(7776^6) ≈ 77.5 bits.
    /// </summary>
    public const double RecommendedEntropyBits = 77.5;
}
