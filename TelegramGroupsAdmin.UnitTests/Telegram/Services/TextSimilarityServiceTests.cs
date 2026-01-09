namespace TelegramGroupsAdmin.UnitTests.Telegram.Services;

using TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Unit tests for TextSimilarityService - Jaccard similarity calculation.
/// This service is used for training data deduplication at insert time (#168).
///
/// Test Coverage:
/// - Identical texts return 1.0 similarity
/// - Completely different texts return 0.0 similarity
/// - Partial overlap returns correct ratio
/// - Case insensitivity
/// - Empty/null handling
/// - Short text handling (single-character tokens filtered)
/// - Punctuation handling
///
/// Threshold Behavior:
/// - 0.90 threshold is used for deduplication (≥90% similar = duplicate)
/// - Tests verify boundary cases around this threshold
/// </summary>
[TestFixture]
public class TextSimilarityServiceTests
{
    private TextSimilarityService _service = null!;

    [SetUp]
    public void Setup()
    {
        _service = new TextSimilarityService();
    }

    #region Basic Similarity Tests

    [Test]
    public void CalculateSimilarity_IdenticalTexts_ReturnsOne()
    {
        // Arrange
        var text1 = "This is a test message for spam detection";
        var text2 = "This is a test message for spam detection";

        // Act
        var similarity = _service.CalculateSimilarity(text1, text2);

        // Assert
        Assert.That(similarity, Is.EqualTo(1.0).Within(0.001));
    }

    [Test]
    public void CalculateSimilarity_CompletelyDifferentTexts_ReturnsZero()
    {
        // Arrange
        var text1 = "Hello world this is great";
        var text2 = "Goodbye planet that was terrible";

        // Act
        var similarity = _service.CalculateSimilarity(text1, text2);

        // Assert - Jaccard similarity with no word overlap = 0
        Assert.That(similarity, Is.EqualTo(0.0).Within(0.001));
    }

    [Test]
    public void CalculateSimilarity_PartialOverlap_ReturnsCorrectRatio()
    {
        // Arrange - 3 shared words (this, is, test), 3 unique to text1 (just, a), 2 unique to text2 (another, sample)
        // Actually: "This is a test" = {this, is, test} (a is single char, filtered)
        // "This is another test sample" = {this, is, another, test, sample}
        // Intersection = {this, is, test} = 3
        // Union = {this, is, test, another, sample} = 5
        // Jaccard = 3/5 = 0.6
        var text1 = "This is a test";
        var text2 = "This is another test sample";

        // Act
        var similarity = _service.CalculateSimilarity(text1, text2);

        // Assert
        Assert.That(similarity, Is.EqualTo(0.6).Within(0.001));
    }

    [Test]
    public void CalculateSimilarity_CaseInsensitive_ReturnsSameResult()
    {
        // Arrange
        var text1 = "HELLO WORLD TEST";
        var text2 = "hello world test";

        // Act
        var similarity = _service.CalculateSimilarity(text1, text2);

        // Assert - Should be identical despite case difference
        Assert.That(similarity, Is.EqualTo(1.0).Within(0.001));
    }

    [Test]
    public void CalculateSimilarity_PunctuationIgnored_ReturnsCorrectRatio()
    {
        // Arrange
        var text1 = "Hello, world! This is a test.";
        var text2 = "Hello world this is test";

        // Act
        var similarity = _service.CalculateSimilarity(text1, text2);

        // Assert - Punctuation stripped, should be identical
        Assert.That(similarity, Is.EqualTo(1.0).Within(0.001));
    }

    #endregion

    #region Edge Cases

    [Test]
    public void CalculateSimilarity_EmptyText1_ReturnsZero()
    {
        // Arrange
        var text1 = "";
        var text2 = "Hello world";

        // Act
        var similarity = _service.CalculateSimilarity(text1, text2);

        // Assert
        Assert.That(similarity, Is.EqualTo(0.0).Within(0.001));
    }

    [Test]
    public void CalculateSimilarity_EmptyText2_ReturnsZero()
    {
        // Arrange
        var text1 = "Hello world";
        var text2 = "";

        // Act
        var similarity = _service.CalculateSimilarity(text1, text2);

        // Assert
        Assert.That(similarity, Is.EqualTo(0.0).Within(0.001));
    }

    [Test]
    public void CalculateSimilarity_BothEmpty_ReturnsZero()
    {
        // Arrange
        var text1 = "";
        var text2 = "";

        // Act
        var similarity = _service.CalculateSimilarity(text1, text2);

        // Assert
        Assert.That(similarity, Is.EqualTo(0.0).Within(0.001));
    }

    [Test]
    public void CalculateSimilarity_NullText1_ReturnsZero()
    {
        // Act
        var similarity = _service.CalculateSimilarity(null!, "Hello world");

        // Assert
        Assert.That(similarity, Is.EqualTo(0.0).Within(0.001));
    }

    [Test]
    public void CalculateSimilarity_NullText2_ReturnsZero()
    {
        // Act
        var similarity = _service.CalculateSimilarity("Hello world", null!);

        // Assert
        Assert.That(similarity, Is.EqualTo(0.0).Within(0.001));
    }

    [Test]
    public void CalculateSimilarity_WhitespaceOnly_ReturnsZero()
    {
        // Arrange
        var text1 = "   \t\n  ";
        var text2 = "Hello world";

        // Act
        var similarity = _service.CalculateSimilarity(text1, text2);

        // Assert
        Assert.That(similarity, Is.EqualTo(0.0).Within(0.001));
    }

    [Test]
    public void CalculateSimilarity_SingleCharacterTokensFiltered_ReturnsCorrectResult()
    {
        // Arrange - Single-character words like "a", "I" are filtered
        // "I am a test" -> {am, test} (2 tokens)
        // "We are a test" -> {we, are, test} (3 tokens)
        // Intersection: {test} = 1
        // Union: {am, test, we, are} = 4
        // Jaccard = 1/4 = 0.25
        var text1 = "I am a test";
        var text2 = "We are a test";

        // Act
        var similarity = _service.CalculateSimilarity(text1, text2);

        // Assert
        Assert.That(similarity, Is.EqualTo(0.25).Within(0.001));
    }

    #endregion

    #region Deduplication Threshold Tests (90% boundary)

    [Test]
    public void CalculateSimilarity_HighlySimilar_AboveThreshold()
    {
        // Arrange - Almost identical text (small variation)
        var text1 = "Click here to win a free prize today instant money";
        var text2 = "Click here to win free prize today instant money now";
        // Shared: click, here, to, win, free, prize, today, instant, money (9)
        // Unique text1: a (filtered)
        // Unique text2: now (1)
        // Jaccard = 9/10 = 0.90

        // Act
        var similarity = _service.CalculateSimilarity(text1, text2);

        // Assert - Should be at or above 0.90 threshold
        Assert.That(similarity, Is.GreaterThanOrEqualTo(0.90), "Highly similar spam should be above dedup threshold");
    }

    [Test]
    public void CalculateSimilarity_ModeratelySimilar_BelowThreshold()
    {
        // Arrange - Similar topic but different wording
        var text1 = "Earn money fast with this simple trick";
        var text2 = "Make cash quickly using easy methods";
        // No overlapping words (synonyms don't count)

        // Act
        var similarity = _service.CalculateSimilarity(text1, text2);

        // Assert - Should be well below 0.90 threshold
        Assert.That(similarity, Is.LessThan(0.90), "Different wording should be below dedup threshold");
    }

    [Test]
    public void CalculateSimilarity_SpamVariants_DetectsNearDuplicates()
    {
        // Arrange - Real spam pattern with minor variation (added word)
        var text1 = "Join our telegram channel for exclusive crypto signals and trading tips";
        var text2 = "Join our telegram channel for exclusive crypto signals and trading tips today";
        // Text1 tokens: join, our, telegram, channel, for, exclusive, crypto, signals, and, trading, tips (11)
        // Text2 tokens: join, our, telegram, channel, for, exclusive, crypto, signals, and, trading, tips, today (12)
        // Intersection: 11, Union: 12
        // Jaccard = 11/12 = 0.917

        // Act
        var similarity = _service.CalculateSimilarity(text1, text2);

        // Assert - Should detect as near-duplicate (>90%)
        Assert.That(similarity, Is.GreaterThan(0.90), "Spam variants with added word should be detected as duplicates");
    }

    #endregion
}
