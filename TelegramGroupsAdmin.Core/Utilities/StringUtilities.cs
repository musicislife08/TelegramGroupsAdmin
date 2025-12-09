namespace TelegramGroupsAdmin.Core.Utilities;

/// <summary>
/// String manipulation and similarity utilities shared across all domain libraries
/// Used for fuzzy matching, duplicate detection, search relevance
/// </summary>
public static class StringUtilities
{
    /// <summary>
    /// Compute Levenshtein distance (edit distance) between two strings
    /// Calculates minimum number of single-character edits (insertions, deletions, substitutions)
    /// required to transform one string into another
    /// </summary>
    /// <param name="source">Source string</param>
    /// <param name="target">Target string to compare against</param>
    /// <returns>Minimum number of edits needed (0 = identical strings)</returns>
    public static int LevenshteinDistance(string source, string target)
    {
        if (string.IsNullOrEmpty(source))
            return target?.Length ?? 0;

        if (string.IsNullOrEmpty(target))
            return source.Length;

        var matrix = new int[source.Length + 1, target.Length + 1];

        // Initialize first row and column
        for (int i = 0; i <= source.Length; i++)
            matrix[i, 0] = i;

        for (int j = 0; j <= target.Length; j++)
            matrix[0, j] = j;

        // Fill matrix using dynamic programming
        for (int i = 1; i <= source.Length; i++)
        {
            for (int j = 1; j <= target.Length; j++)
            {
                var cost = source[i - 1] == target[j - 1] ? 0 : 1;

                matrix[i, j] = Math.Min(
                    Math.Min(
                        matrix[i - 1, j] + 1,      // Deletion
                        matrix[i, j - 1] + 1),     // Insertion
                    matrix[i - 1, j - 1] + cost);  // Substitution
            }
        }

        return matrix[source.Length, target.Length];
    }

    /// <summary>
    /// Calculate name similarity using normalized Levenshtein distance
    /// Combines first and last names, normalizes case, and computes similarity score
    /// </summary>
    /// <param name="firstName1">First name of first person</param>
    /// <param name="lastName1">Last name of first person</param>
    /// <param name="firstName2">First name of second person</param>
    /// <param name="lastName2">Last name of second person</param>
    /// <returns>Similarity score from 0.0 (completely different) to 1.0 (identical)</returns>
    public static double CalculateNameSimilarity(
        string? firstName1, string? lastName1,
        string? firstName2, string? lastName2)
    {
        // Normalize names (combine first+last, lowercase, trim whitespace)
        var name1 = $"{firstName1} {lastName1}".ToLowerInvariant().Trim();
        var name2 = $"{firstName2} {lastName2}".ToLowerInvariant().Trim();

        if (string.IsNullOrWhiteSpace(name1) || string.IsNullOrWhiteSpace(name2))
            return 0.0;

        var distance = LevenshteinDistance(name1, name2);
        var maxLength = Math.Max(name1.Length, name2.Length);

        // Convert distance to similarity (0 distance = 100% similar)
        return 1.0 - ((double)distance / maxLength);
    }

    /// <summary>
    /// Calculate similarity between two strings using normalized Levenshtein distance
    /// </summary>
    /// <param name="string1">First string</param>
    /// <param name="string2">Second string</param>
    /// <returns>Similarity score from 0.0 (completely different) to 1.0 (identical)</returns>
    public static double CalculateStringSimilarity(string? string1, string? string2)
    {
        if (string.IsNullOrWhiteSpace(string1) || string.IsNullOrWhiteSpace(string2))
            return 0.0;

        // Normalize (lowercase, trim)
        var s1 = string1.ToLowerInvariant().Trim();
        var s2 = string2.ToLowerInvariant().Trim();

        var distance = LevenshteinDistance(s1, s2);
        var maxLength = Math.Max(s1.Length, s2.Length);

        // Convert distance to similarity (0 distance = 100% similar)
        return 1.0 - ((double)distance / maxLength);
    }
}
