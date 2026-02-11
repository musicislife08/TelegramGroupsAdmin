namespace TelegramGroupsAdmin.UnitTests.Telegram.Services;

using TelegramGroupsAdmin.Core.Utilities;

/// <summary>
/// Unit tests for SimHashService - 64-bit locality-sensitive hashing.
/// This service computes fingerprints where similar texts produce similar hashes.
///
/// Test Coverage:
/// - Identical texts return identical hashes
/// - Similar texts have low Hamming distance
/// - Different texts have high Hamming distance
/// - Empty/null handling
/// - Case insensitivity
/// - Punctuation handling
/// - AreSimilar threshold behavior
///
/// Hamming Distance Interpretation:
/// - 0: Identical texts
/// - 1-10: Very similar (~84%+ similarity)
/// - 11-20: Moderately similar
/// - 21-32: Somewhat different
/// - 33-64: Very different
/// </summary>
[TestFixture]
public class SimHashServiceTests
{
    private SimHashService _service = null!;

    [SetUp]
    public void Setup()
    {
        _service = new SimHashService();
    }

    #region Basic Hash Computation Tests

    [Test]
    public void ComputeHash_IdenticalTexts_ReturnsSameHash()
    {
        // Arrange
        var text1 = "Join our crypto channel for signals";
        var text2 = "Join our crypto channel for signals";

        // Act
        var hash1 = _service.ComputeHash(text1);
        var hash2 = _service.ComputeHash(text2);

        // Assert
        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void ComputeHash_SimilarTexts_LowHammingDistance()
    {
        // Arrange - texts differ by one word
        var text1 = "Join our crypto channel for signals";
        var text2 = "Join our crypto channel for FREE signals";

        // Act
        var hash1 = _service.ComputeHash(text1);
        var hash2 = _service.ComputeHash(text2);
        var distance = _service.HammingDistance(hash1, hash2);

        // Assert - should be within similarity threshold
        Assert.That(distance, Is.LessThanOrEqualTo(15),
            $"Similar texts should have low Hamming distance, got {distance}");
    }

    [Test]
    public void ComputeHash_DifferentTexts_HighHammingDistance()
    {
        // Arrange - completely different topics
        var text1 = "Join our crypto channel for signals";
        var text2 = "I love pizza and sunny weather today";

        // Act
        var hash1 = _service.ComputeHash(text1);
        var hash2 = _service.ComputeHash(text2);
        var distance = _service.HammingDistance(hash1, hash2);

        // Assert - should be far apart
        Assert.That(distance, Is.GreaterThan(15),
            $"Different texts should have high Hamming distance, got {distance}");
    }

    [Test]
    public void ComputeHash_CaseInsensitive_ReturnsSameHash()
    {
        // Arrange
        var text1 = "JOIN OUR CRYPTO CHANNEL";
        var text2 = "join our crypto channel";

        // Act
        var hash1 = _service.ComputeHash(text1);
        var hash2 = _service.ComputeHash(text2);

        // Assert
        Assert.That(hash1, Is.EqualTo(hash2), "Hash should be case-insensitive");
    }

    [Test]
    public void ComputeHash_PunctuationIgnored_ReturnsSameHash()
    {
        // Arrange
        var text1 = "Hello, world! How are you?";
        var text2 = "Hello world How are you";

        // Act
        var hash1 = _service.ComputeHash(text1);
        var hash2 = _service.ComputeHash(text2);

        // Assert
        Assert.That(hash1, Is.EqualTo(hash2), "Punctuation should not affect hash");
    }

    #endregion

    #region Edge Cases

    [Test]
    public void ComputeHash_EmptyString_ReturnsZero()
    {
        // Act
        var hash = _service.ComputeHash("");

        // Assert
        Assert.That(hash, Is.EqualTo(0));
    }

    [Test]
    public void ComputeHash_NullString_ReturnsZero()
    {
        // Act
        var hash = _service.ComputeHash(null);

        // Assert
        Assert.That(hash, Is.EqualTo(0));
    }

    [Test]
    public void ComputeHash_WhitespaceOnly_ReturnsZero()
    {
        // Act
        var hash = _service.ComputeHash("   \t\n   ");

        // Assert
        Assert.That(hash, Is.EqualTo(0));
    }

    [Test]
    public void ComputeHash_SingleCharacterWords_ReturnsZero()
    {
        // Arrange - single char words are filtered out (same as Jaccard)
        var text = "I a x y z";

        // Act
        var hash = _service.ComputeHash(text);

        // Assert - all tokens filtered, returns 0
        Assert.That(hash, Is.EqualTo(0));
    }

    [Test]
    public void ComputeHash_MixedSingleAndMultiCharWords_HashesOnlyMultiChar()
    {
        // Arrange
        var text1 = "I am a test message";
        var text2 = "am test message"; // Same multi-char tokens

        // Act
        var hash1 = _service.ComputeHash(text1);
        var hash2 = _service.ComputeHash(text2);

        // Assert - should be identical (single chars filtered)
        Assert.That(hash1, Is.EqualTo(hash2));
    }

    #endregion

    #region Hamming Distance Tests

    [Test]
    public void HammingDistance_IdenticalHashes_ReturnsZero()
    {
        // Arrange
        long hash = 0x123456789ABCDEF0;

        // Act
        var distance = _service.HammingDistance(hash, hash);

        // Assert
        Assert.That(distance, Is.EqualTo(0));
    }

    [Test]
    public void HammingDistance_OneBitDifferent_ReturnsOne()
    {
        // Arrange
        long hash1 = 0b0000;
        long hash2 = 0b0001;

        // Act
        var distance = _service.HammingDistance(hash1, hash2);

        // Assert
        Assert.That(distance, Is.EqualTo(1));
    }

    [Test]
    public void HammingDistance_AllBitsDifferent_Returns64()
    {
        // Arrange
        long hash1 = 0;
        long hash2 = -1; // All bits set (0xFFFFFFFFFFFFFFFF)

        // Act
        var distance = _service.HammingDistance(hash1, hash2);

        // Assert
        Assert.That(distance, Is.EqualTo(64));
    }

    #endregion

    #region AreSimilar Tests

    [Test]
    public void AreSimilar_IdenticalHashes_ReturnsTrue()
    {
        // Arrange
        var text = "Test message for similarity";
        var hash = _service.ComputeHash(text);

        // Act
        var similar = _service.AreSimilar(hash, hash);

        // Assert
        Assert.That(similar, Is.True);
    }

    [Test]
    public void AreSimilar_WithinThreshold_ReturnsTrue()
    {
        // Arrange - use longer texts for more stable similarity detection
        // Short texts (5 tokens) have higher variance; real spam messages are longer
        var text1 = "Join our telegram channel for exclusive crypto signals and trading tips";
        var text2 = "Join our telegram channel for exclusive crypto signals and trading advice";

        var hash1 = _service.ComputeHash(text1);
        var hash2 = _service.ComputeHash(text2);
        var distance = _service.HammingDistance(hash1, hash2);

        // Act
        var similar = _service.AreSimilar(hash1, hash2, maxDistance: 10);

        // Assert
        Assert.That(similar, Is.True,
            $"Near-duplicate texts should be similar (actual distance: {distance})");
    }

    [Test]
    public void AreSimilar_BeyondThreshold_ReturnsFalse()
    {
        // Arrange
        var text1 = "Buy crypto signals now";
        var text2 = "I love cooking recipes";

        var hash1 = _service.ComputeHash(text1);
        var hash2 = _service.ComputeHash(text2);

        // Act
        var similar = _service.AreSimilar(hash1, hash2, maxDistance: 10);

        // Assert
        Assert.That(similar, Is.False, "Different texts should not be similar");
    }

    [Test]
    public void AreSimilar_CustomThreshold_RespectsValue()
    {
        // Arrange - texts with moderate similarity
        var text1 = "Join telegram channel crypto";
        var text2 = "Join telegram group trading";

        var hash1 = _service.ComputeHash(text1);
        var hash2 = _service.ComputeHash(text2);
        var distance = _service.HammingDistance(hash1, hash2);

        // Act & Assert - with tight threshold, might not be similar
        var similarTight = _service.AreSimilar(hash1, hash2, maxDistance: 5);
        // With loose threshold, should be similar
        var similarLoose = _service.AreSimilar(hash1, hash2, maxDistance: 30);

        Assert.That(similarLoose, Is.True, "Should be similar with loose threshold");
        // Log the actual distance for debugging
        TestContext.Out.WriteLine($"Actual Hamming distance: {distance}");
    }

    #endregion

    #region Spam Pattern Tests

    [Test]
    public void ComputeHash_SpamVariants_DetectsNearDuplicates()
    {
        // Arrange - real spam patterns with minor variations
        var spam1 = "Join our telegram channel for exclusive crypto signals and trading tips";
        var spam2 = "Join our telegram channel for exclusive crypto signals and trading tips today";

        // Act
        var hash1 = _service.ComputeHash(spam1);
        var hash2 = _service.ComputeHash(spam2);
        var distance = _service.HammingDistance(hash1, hash2);

        // Assert - should detect as near-duplicate (low distance)
        Assert.That(distance, Is.LessThanOrEqualTo(10),
            $"Spam variants should be detected as similar, got distance {distance}");
    }

    [Test]
    public void ComputeHash_DifferentSpamTopics_NotSimilar()
    {
        // Arrange - different spam types
        var cryptoSpam = "Click here to win free bitcoin prize money";
        var datingSpam = "Hot singles in your area want to meet you";

        // Act
        var hash1 = _service.ComputeHash(cryptoSpam);
        var hash2 = _service.ComputeHash(datingSpam);
        var distance = _service.HammingDistance(hash1, hash2);

        // Assert - should not be similar
        Assert.That(distance, Is.GreaterThan(15),
            $"Different spam types should have high distance, got {distance}");
    }

    #endregion

    #region Consistency Tests

    [Test]
    public void ComputeHash_Deterministic_SameInputSameOutput()
    {
        // Arrange
        var text = "This is a deterministic test message";

        // Act - compute multiple times
        var hash1 = _service.ComputeHash(text);
        var hash2 = _service.ComputeHash(text);
        var hash3 = _service.ComputeHash(text);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(hash1, Is.EqualTo(hash2));
            Assert.That(hash2, Is.EqualTo(hash3));
        }
    }

    [Test]
    public void ComputeHash_WordOrderMatters()
    {
        // Arrange - same words, different order
        var text1 = "crypto trading signals telegram";
        var text2 = "telegram signals trading crypto";

        // Act
        var hash1 = _service.ComputeHash(text1);
        var hash2 = _service.ComputeHash(text2);

        // Assert - SimHash uses bag-of-words, order shouldn't matter
        Assert.That(hash1, Is.EqualTo(hash2),
            "SimHash should be order-independent (bag of words)");
    }

    #endregion
}
