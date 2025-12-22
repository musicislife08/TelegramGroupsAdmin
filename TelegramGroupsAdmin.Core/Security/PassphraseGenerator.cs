using System.Reflection;
using System.Security.Cryptography;

namespace TelegramGroupsAdmin.Core.Security;

/// <summary>
/// Generates secure, memorable passphrases using the EFF Large Wordlist.
/// Uses cryptographically secure random number generation to select words from a 7,776-word list.
/// Each word provides ~12.9 bits of entropy.
/// </summary>
/// <remarks>
/// Wordlist: EFF Large Wordlist (2016) - https://www.eff.org/deeplinks/2016/07/new-wordlists-random-passphrases
/// Security: 6 words = 77.5 bits of entropy, exceeds AES-256 effective security with PBKDF2 key stretching.
/// </remarks>
public static class PassphraseGenerator
{
    private const int MinimumWords = SecurityConstants.MinimumPassphraseWords;
    private const int RecommendedWords = SecurityConstants.RecommendedPassphraseWords;

    /// <summary>
    /// Lazy-loaded wordlist from embedded resource.
    /// Thread-safe initialization ensures single load across application lifetime.
    /// </summary>
    private static readonly Lazy<string[]> Wordlist = new(LoadEmbeddedWordlist);

    /// <summary>
    /// Wordlist size - dynamically determined from loaded wordlist to avoid manual updates when filtering.
    /// Filtered from original EFF 7,776 words (removed 477 problematic words via AI review).
    /// </summary>
    private static int WordlistSize => Wordlist.Value.Length;

    /// <summary>
    /// Generates a secure, memorable passphrase using the EFF Large Wordlist.
    /// </summary>
    /// <param name="wordCount">Number of words (minimum 5 for security, recommended 6). Default: 6 words.</param>
    /// <param name="separator">Character(s) between words. Default: dash (-).</param>
    /// <returns>A passphrase like "correct-horse-battery-staple-meadow-fortune"</returns>
    /// <exception cref="ArgumentException">Thrown if wordCount is less than 5.</exception>
    /// <remarks>
    /// Entropy by word count:
    /// - 5 words = 64.6 bits (minimum acceptable)
    /// - 6 words = 77.5 bits (recommended, default)
    /// - 7 words = 90.4 bits (high security)
    /// - 8 words = 103.2 bits (maximum)
    /// </remarks>
    public static string Generate(int wordCount = RecommendedWords, string separator = "-")
    {
        if (wordCount < MinimumWords)
        {
            throw new ArgumentException(
                $"Minimum {MinimumWords} words required for security (64.6 bits entropy). " +
                $"Recommended: {RecommendedWords} words (77.5 bits).",
                nameof(wordCount));
        }

        var words = new string[wordCount];
        for (int i = 0; i < wordCount; i++)
        {
            // Use cryptographically secure random number generation
            var index = RandomNumberGenerator.GetInt32(0, WordlistSize);
            words[i] = Wordlist.Value[index];
        }

        return string.Join(separator, words);
    }

    /// <summary>
    /// Calculates the entropy (in bits) for a given word count.
    /// Formula: log2(7776^wordCount) = wordCount * 12.9 bits
    /// </summary>
    public static double CalculateEntropy(int wordCount)
    {
        return wordCount * Math.Log2(WordlistSize);
    }

    /// <summary>
    /// Loads the EFF Large Wordlist from embedded resource.
    /// </summary>
    private static string[] LoadEmbeddedWordlist()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "TelegramGroupsAdmin.Core.Security.eff_large_wordlist.txt";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            throw new InvalidOperationException(
                $"Embedded resource '{resourceName}' not found. " +
                "Ensure eff_large_wordlist.txt is marked as EmbeddedResource in .csproj");
        }

        using var reader = new StreamReader(stream);
        var words = new List<string>();

        while (reader.ReadLine() is { } line)
        {
            var word = line.Trim();
            if (!string.IsNullOrEmpty(word))
            {
                words.Add(word);
            }
        }

        // Validate we have enough words for security (minimum 5000 for 77+ bits entropy with 6 words)
        if (words.Count < SecurityConstants.MinimumWordlistSize)
        {
            throw new InvalidOperationException(
                $"Wordlist too small for security. Minimum {SecurityConstants.MinimumWordlistSize} words required, got {words.Count}");
        }

        return words.ToArray();
    }
}
