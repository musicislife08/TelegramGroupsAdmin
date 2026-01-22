using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.UnitTests.Core.Utilities;

/// <summary>
/// Unit tests for StringUtilities.
/// Tests Levenshtein distance calculation and string similarity scoring
/// used for fuzzy matching, duplicate detection, and impersonation detection.
/// </summary>
[TestFixture]
public class StringUtilitiesTests
{
    private const double Tolerance = 0.0001;

    #region LevenshteinDistance - Identical Strings

    [Test]
    public void LevenshteinDistance_IdenticalStrings_ReturnsZero()
    {
        var result = StringUtilities.LevenshteinDistance("hello", "hello");

        Assert.That(result, Is.EqualTo(0));
    }

    [Test]
    public void LevenshteinDistance_IdenticalSingleChar_ReturnsZero()
    {
        var result = StringUtilities.LevenshteinDistance("a", "a");

        Assert.That(result, Is.EqualTo(0));
    }

    [Test]
    public void LevenshteinDistance_IdenticalLongString_ReturnsZero()
    {
        var longString = "This is a longer test string with multiple words and special chars!";
        var result = StringUtilities.LevenshteinDistance(longString, longString);

        Assert.That(result, Is.EqualTo(0));
    }

    #endregion

    #region LevenshteinDistance - Empty and Null Strings

    [Test]
    public void LevenshteinDistance_EmptyToNonEmpty_ReturnsLength()
    {
        var result = StringUtilities.LevenshteinDistance("", "hello");

        Assert.That(result, Is.EqualTo(5));
    }

    [Test]
    public void LevenshteinDistance_NonEmptyToEmpty_ReturnsLength()
    {
        var result = StringUtilities.LevenshteinDistance("hello", "");

        Assert.That(result, Is.EqualTo(5));
    }

    [Test]
    public void LevenshteinDistance_BothEmpty_ReturnsZero()
    {
        var result = StringUtilities.LevenshteinDistance("", "");

        Assert.That(result, Is.EqualTo(0));
    }

    [Test]
    public void LevenshteinDistance_NullSource_ReturnsTargetLength()
    {
        var result = StringUtilities.LevenshteinDistance(null!, "hello");

        Assert.That(result, Is.EqualTo(5));
    }

    [Test]
    public void LevenshteinDistance_NullTarget_ReturnsSourceLength()
    {
        var result = StringUtilities.LevenshteinDistance("hello", null!);

        Assert.That(result, Is.EqualTo(5));
    }

    [Test]
    public void LevenshteinDistance_BothNull_ReturnsZero()
    {
        var result = StringUtilities.LevenshteinDistance(null!, null!);

        Assert.That(result, Is.EqualTo(0));
    }

    #endregion

    #region LevenshteinDistance - Single Character Operations

    [Test]
    public void LevenshteinDistance_SingleCharDifference_ReturnsOne()
    {
        // "cat" vs "bat" - single substitution
        var result = StringUtilities.LevenshteinDistance("cat", "bat");

        Assert.That(result, Is.EqualTo(1));
    }

    [Test]
    public void LevenshteinDistance_SingleInsertion_ReturnsOne()
    {
        // "cat" vs "cats" - single insertion
        var result = StringUtilities.LevenshteinDistance("cat", "cats");

        Assert.That(result, Is.EqualTo(1));
    }

    [Test]
    public void LevenshteinDistance_SingleDeletion_ReturnsOne()
    {
        // "cats" vs "cat" - single deletion
        var result = StringUtilities.LevenshteinDistance("cats", "cat");

        Assert.That(result, Is.EqualTo(1));
    }

    #endregion

    #region LevenshteinDistance - Multiple Operations

    [Test]
    public void LevenshteinDistance_CompletelyDifferent_ReturnsMaxLength()
    {
        // "abc" vs "xyz" - all characters different
        var result = StringUtilities.LevenshteinDistance("abc", "xyz");

        Assert.That(result, Is.EqualTo(3));
    }

    [Test]
    public void LevenshteinDistance_DifferentLengths_CalculatesCorrectly()
    {
        // "kitten" vs "sitting" - classic example
        // kitten -> sitten (substitution) -> sittin (substitution) -> sitting (insertion)
        var result = StringUtilities.LevenshteinDistance("kitten", "sitting");

        Assert.That(result, Is.EqualTo(3));
    }

    [TestCase("abc", "abc", ExpectedResult = 0)]
    [TestCase("abc", "abd", ExpectedResult = 1)]
    [TestCase("abc", "ab", ExpectedResult = 1)]
    [TestCase("abc", "abcd", ExpectedResult = 1)]
    [TestCase("abc", "def", ExpectedResult = 3)]
    [TestCase("abc", "dabc", ExpectedResult = 1)]
    [TestCase("abc", "abdc", ExpectedResult = 1)]
    public int LevenshteinDistance_KnownPatterns_ReturnsExpected(string source, string target)
    {
        return StringUtilities.LevenshteinDistance(source, target);
    }

    #endregion

    #region LevenshteinDistance - Case Sensitivity

    [Test]
    public void LevenshteinDistance_IsCaseSensitive()
    {
        // "Hello" vs "hello" - different case
        var result = StringUtilities.LevenshteinDistance("Hello", "hello");

        Assert.That(result, Is.EqualTo(1));
    }

    [Test]
    public void LevenshteinDistance_AllCapsVsLower_ReturnsDifference()
    {
        // "ABC" vs "abc" - all different case
        var result = StringUtilities.LevenshteinDistance("ABC", "abc");

        Assert.That(result, Is.EqualTo(3));
    }

    #endregion

    #region LevenshteinDistance - Special Characters and Unicode

    [Test]
    public void LevenshteinDistance_WithSpaces_CalculatesCorrectly()
    {
        var result = StringUtilities.LevenshteinDistance("hello world", "hello  world");

        Assert.That(result, Is.EqualTo(1)); // Extra space
    }

    [Test]
    public void LevenshteinDistance_WithNumbers_CalculatesCorrectly()
    {
        var result = StringUtilities.LevenshteinDistance("test123", "test124");

        Assert.That(result, Is.EqualTo(1));
    }

    [Test]
    public void LevenshteinDistance_WithEmoji_CalculatesCorrectly()
    {
        // Each emoji counts as a single character difference
        var result = StringUtilities.LevenshteinDistance("hello😀", "hello😊");

        Assert.That(result, Is.EqualTo(1));
    }

    [Test]
    public void LevenshteinDistance_Unicode_CalculatesCorrectly()
    {
        // Non-ASCII characters
        var result = StringUtilities.LevenshteinDistance("café", "cafe");

        Assert.That(result, Is.EqualTo(1)); // é vs e
    }

    #endregion

    #region CalculateStringSimilarity - Basic Cases

    [Test]
    public void CalculateStringSimilarity_IdenticalStrings_ReturnsOne()
    {
        var result = StringUtilities.CalculateStringSimilarity("hello", "hello");

        Assert.That(result, Is.EqualTo(1.0).Within(Tolerance));
    }

    [Test]
    public void CalculateStringSimilarity_CompletelyDifferent_ReturnsZero()
    {
        // Same length, all different
        var result = StringUtilities.CalculateStringSimilarity("abc", "xyz");

        Assert.That(result, Is.EqualTo(0.0).Within(Tolerance));
    }

    [Test]
    public void CalculateStringSimilarity_OneDifference_ReturnsPartialMatch()
    {
        // "hello" vs "hallo" - 1 char different out of 5
        var result = StringUtilities.CalculateStringSimilarity("hello", "hallo");

        // 1 - (1/5) = 0.8
        Assert.That(result, Is.EqualTo(0.8).Within(Tolerance));
    }

    #endregion

    #region CalculateStringSimilarity - Null and Empty Handling

    [Test]
    public void CalculateStringSimilarity_NullFirst_ReturnsZero()
    {
        var result = StringUtilities.CalculateStringSimilarity(null, "hello");

        Assert.That(result, Is.EqualTo(0.0).Within(Tolerance));
    }

    [Test]
    public void CalculateStringSimilarity_NullSecond_ReturnsZero()
    {
        var result = StringUtilities.CalculateStringSimilarity("hello", null);

        Assert.That(result, Is.EqualTo(0.0).Within(Tolerance));
    }

    [Test]
    public void CalculateStringSimilarity_BothNull_ReturnsZero()
    {
        var result = StringUtilities.CalculateStringSimilarity(null, null);

        Assert.That(result, Is.EqualTo(0.0).Within(Tolerance));
    }

    [Test]
    public void CalculateStringSimilarity_EmptyFirst_ReturnsZero()
    {
        var result = StringUtilities.CalculateStringSimilarity("", "hello");

        Assert.That(result, Is.EqualTo(0.0).Within(Tolerance));
    }

    [Test]
    public void CalculateStringSimilarity_EmptySecond_ReturnsZero()
    {
        var result = StringUtilities.CalculateStringSimilarity("hello", "");

        Assert.That(result, Is.EqualTo(0.0).Within(Tolerance));
    }

    [Test]
    public void CalculateStringSimilarity_WhitespaceOnly_ReturnsZero()
    {
        var result = StringUtilities.CalculateStringSimilarity("   ", "hello");

        Assert.That(result, Is.EqualTo(0.0).Within(Tolerance));
    }

    #endregion

    #region CalculateStringSimilarity - Case Insensitivity

    [Test]
    public void CalculateStringSimilarity_DifferentCase_ReturnsOne()
    {
        // Should normalize to lowercase
        var result = StringUtilities.CalculateStringSimilarity("Hello", "hello");

        Assert.That(result, Is.EqualTo(1.0).Within(Tolerance));
    }

    [Test]
    public void CalculateStringSimilarity_AllCapsVsLower_ReturnsOne()
    {
        var result = StringUtilities.CalculateStringSimilarity("HELLO WORLD", "hello world");

        Assert.That(result, Is.EqualTo(1.0).Within(Tolerance));
    }

    [Test]
    public void CalculateStringSimilarity_MixedCase_ReturnsOne()
    {
        var result = StringUtilities.CalculateStringSimilarity("HeLLo", "hElLO");

        Assert.That(result, Is.EqualTo(1.0).Within(Tolerance));
    }

    #endregion

    #region CalculateStringSimilarity - Whitespace Handling

    [Test]
    public void CalculateStringSimilarity_LeadingWhitespace_IsTrimmed()
    {
        var result = StringUtilities.CalculateStringSimilarity("  hello", "hello");

        Assert.That(result, Is.EqualTo(1.0).Within(Tolerance));
    }

    [Test]
    public void CalculateStringSimilarity_TrailingWhitespace_IsTrimmed()
    {
        var result = StringUtilities.CalculateStringSimilarity("hello  ", "hello");

        Assert.That(result, Is.EqualTo(1.0).Within(Tolerance));
    }

    [Test]
    public void CalculateStringSimilarity_BothWithWhitespace_TrimsBoth()
    {
        var result = StringUtilities.CalculateStringSimilarity("  hello  ", "  hello  ");

        Assert.That(result, Is.EqualTo(1.0).Within(Tolerance));
    }

    #endregion

    #region CalculateStringSimilarity - Range Validation

    [Test]
    public void CalculateStringSimilarity_AlwaysReturnsBetweenZeroAndOne()
    {
        // Test various inputs to ensure range is always [0, 1]
        var testCases = new[]
        {
            ("a", "b"),
            ("hello", "world"),
            ("short", "verylongstring"),
            ("abc", "xyz"),
            ("test", "test"),
            ("foo", "bar")
        };

        foreach (var (s1, s2) in testCases)
        {
            var result = StringUtilities.CalculateStringSimilarity(s1, s2);
            Assert.That(result, Is.GreaterThanOrEqualTo(0.0), $"Failed for ({s1}, {s2})");
            Assert.That(result, Is.LessThanOrEqualTo(1.0), $"Failed for ({s1}, {s2})");
        }
    }

    [Test]
    public void CalculateStringSimilarity_DifferentLengths_CalculatesCorrectly()
    {
        // "hello" (5) vs "helloworld" (10) - need 5 insertions
        // distance = 5, maxLength = 10, similarity = 1 - 5/10 = 0.5
        var result = StringUtilities.CalculateStringSimilarity("hello", "helloworld");

        Assert.That(result, Is.EqualTo(0.5).Within(Tolerance));
    }

    #endregion

    #region CalculateNameSimilarity - Basic Cases

    [Test]
    public void CalculateNameSimilarity_IdenticalNames_ReturnsOne()
    {
        var result = StringUtilities.CalculateNameSimilarity("John", "Doe", "John", "Doe");

        Assert.That(result, Is.EqualTo(1.0).Within(Tolerance));
    }

    [Test]
    public void CalculateNameSimilarity_CompletelyDifferent_ReturnsLowScore()
    {
        var result = StringUtilities.CalculateNameSimilarity("John", "Doe", "Alice", "Smith");

        Assert.That(result, Is.LessThan(0.5));
    }

    [Test]
    public void CalculateNameSimilarity_SameFirstName_DifferentLastName_ReturnsPartialMatch()
    {
        var result = StringUtilities.CalculateNameSimilarity("John", "Doe", "John", "Smith");

        // Should have some similarity due to shared first name
        Assert.That(result, Is.GreaterThan(0.0));
        Assert.That(result, Is.LessThan(1.0));
    }

    [Test]
    public void CalculateNameSimilarity_SimilarNames_ReturnsHighScore()
    {
        // Minor typo in name: "john smith" vs "jon smith"
        // Distance = 1 (missing 'h'), maxLength = 10, similarity = 1 - 1/10 = 0.9
        var result = StringUtilities.CalculateNameSimilarity("John", "Smith", "Jon", "Smith");

        Assert.That(result, Is.GreaterThanOrEqualTo(0.9));
    }

    #endregion

    #region CalculateNameSimilarity - Null Handling

    [Test]
    public void CalculateNameSimilarity_NullFirstNames_ComparesLastNamesOnly()
    {
        var result = StringUtilities.CalculateNameSimilarity(null, "Smith", null, "Smith");

        Assert.That(result, Is.EqualTo(1.0).Within(Tolerance));
    }

    [Test]
    public void CalculateNameSimilarity_NullLastNames_ComparesFirstNamesOnly()
    {
        var result = StringUtilities.CalculateNameSimilarity("John", null, "John", null);

        Assert.That(result, Is.EqualTo(1.0).Within(Tolerance));
    }

    [Test]
    public void CalculateNameSimilarity_AllNull_ReturnsZero()
    {
        // Both names are empty after trimming
        var result = StringUtilities.CalculateNameSimilarity(null, null, null, null);

        Assert.That(result, Is.EqualTo(0.0).Within(Tolerance));
    }

    [Test]
    public void CalculateNameSimilarity_OneFullyNull_ReturnsZero()
    {
        var result = StringUtilities.CalculateNameSimilarity("John", "Doe", null, null);

        Assert.That(result, Is.EqualTo(0.0).Within(Tolerance));
    }

    #endregion

    #region CalculateNameSimilarity - Case Insensitivity

    [Test]
    public void CalculateNameSimilarity_DifferentCase_ReturnsOne()
    {
        var result = StringUtilities.CalculateNameSimilarity("JOHN", "DOE", "john", "doe");

        Assert.That(result, Is.EqualTo(1.0).Within(Tolerance));
    }

    [Test]
    public void CalculateNameSimilarity_MixedCase_ReturnsOne()
    {
        var result = StringUtilities.CalculateNameSimilarity("JoHn", "DoE", "jOhN", "dOe");

        Assert.That(result, Is.EqualTo(1.0).Within(Tolerance));
    }

    #endregion

    #region CalculateNameSimilarity - Impersonation Detection Scenarios

    [Test]
    public void CalculateNameSimilarity_TyposquattingAttempt_ReturnsHighScore()
    {
        // Attacker uses similar name with typo (impersonation attempt)
        var result = StringUtilities.CalculateNameSimilarity("Admin", "Support", "Admln", "Support");

        // "Admin" vs "Admln" is visually similar but different
        Assert.That(result, Is.GreaterThan(0.8));
    }

    [Test]
    public void CalculateNameSimilarity_NameSwap_DetectsAsDifferent()
    {
        // Names swapped (first/last): "john smith" vs "smith john"
        // Even though they contain same characters, Levenshtein sees them as different
        // This is expected - the algorithm measures edit distance, not character overlap
        var result = StringUtilities.CalculateNameSimilarity("John", "Smith", "Smith", "John");

        // Swapped names will have a low similarity score
        Assert.That(result, Is.LessThan(0.5));
    }

    [Test]
    public void CalculateNameSimilarity_ExtraCharacters_Detected()
    {
        // Extra characters added to look similar
        var result = StringUtilities.CalculateNameSimilarity("Admin", "", "Admin_", "");

        Assert.That(result, Is.GreaterThan(0.8));
    }

    #endregion

    #region Integration - Symmetry Tests

    [Test]
    public void LevenshteinDistance_IsSymmetric()
    {
        var result1 = StringUtilities.LevenshteinDistance("hello", "world");
        var result2 = StringUtilities.LevenshteinDistance("world", "hello");

        Assert.That(result1, Is.EqualTo(result2));
    }

    [Test]
    public void CalculateStringSimilarity_IsSymmetric()
    {
        var result1 = StringUtilities.CalculateStringSimilarity("hello", "world");
        var result2 = StringUtilities.CalculateStringSimilarity("world", "hello");

        Assert.That(result1, Is.EqualTo(result2).Within(Tolerance));
    }

    [Test]
    public void CalculateNameSimilarity_IsSymmetric()
    {
        var result1 = StringUtilities.CalculateNameSimilarity("John", "Doe", "Jane", "Smith");
        var result2 = StringUtilities.CalculateNameSimilarity("Jane", "Smith", "John", "Doe");

        Assert.That(result1, Is.EqualTo(result2).Within(Tolerance));
    }

    #endregion
}
