using TelegramGroupsAdmin.Core;
using TelegramGroupsAdmin.Core.Security;

namespace TelegramGroupsAdmin.UnitTests.Core.Security;

/// <summary>
/// Unit tests for PassphraseGenerator.
/// Tests secure passphrase generation using the EFF Large Wordlist.
/// </summary>
[TestFixture]
public class PassphraseGeneratorTests
{
    #region Generate - Basic Tests

    [Test]
    public void Generate_Default_Returns6Words()
    {
        var passphrase = PassphraseGenerator.Generate();

        var words = passphrase.Split('-');
        Assert.That(words.Length, Is.EqualTo(6));
    }

    [Test]
    public void Generate_Default_UsesDashSeparator()
    {
        var passphrase = PassphraseGenerator.Generate();

        Assert.That(passphrase, Does.Contain("-"));
    }

    [Test]
    public void Generate_CustomWordCount_ReturnsCorrectCount()
    {
        var passphrase = PassphraseGenerator.Generate(wordCount: 7);

        var words = passphrase.Split('-');
        Assert.That(words.Length, Is.EqualTo(7));
    }

    [Test]
    public void Generate_CustomSeparator_UsesSeparator()
    {
        var passphrase = PassphraseGenerator.Generate(separator: " ");

        Assert.That(passphrase, Does.Contain(" "));
        Assert.That(passphrase, Does.Not.Contain("-"));
    }

    [Test]
    public void Generate_MinimumWords_Succeeds()
    {
        var passphrase = PassphraseGenerator.Generate(wordCount: SecurityConstants.MinimumPassphraseWords);

        var words = passphrase.Split('-');
        Assert.That(words.Length, Is.EqualTo(5));
    }

    [Test]
    public void Generate_BelowMinimum_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            PassphraseGenerator.Generate(wordCount: 4));
    }

    [Test]
    public void Generate_BelowMinimum_ExceptionMessage_ContainsMinimum()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            PassphraseGenerator.Generate(wordCount: 3));

        Assert.That(exception!.Message, Does.Contain("5"));
        Assert.That(exception.Message, Does.Contain("64.6"));
    }

    #endregion

    #region Generate - Randomness Tests

    [Test]
    public void Generate_ConsecutiveCalls_ReturnDifferent()
    {
        var passphrase1 = PassphraseGenerator.Generate();
        var passphrase2 = PassphraseGenerator.Generate();

        // Extremely unlikely to be the same with 77.5 bits of entropy
        Assert.That(passphrase1, Is.Not.EqualTo(passphrase2));
    }

    [Test]
    public void Generate_MultipleGenerated_AllDifferent()
    {
        var passphrases = new HashSet<string>();

        for (int i = 0; i < 100; i++)
        {
            var passphrase = PassphraseGenerator.Generate();
            passphrases.Add(passphrase);
        }

        // All 100 should be unique
        Assert.That(passphrases.Count, Is.EqualTo(100));
    }

    [Test]
    public void Generate_WordsAreFromWordlist()
    {
        var passphrase = PassphraseGenerator.Generate();
        var words = passphrase.Split('-');

        foreach (var word in words)
        {
            // Words should be lowercase and contain only letters
            Assert.That(word, Does.Match("^[a-z]+$"),
                $"Word '{word}' should be lowercase letters only");
        }
    }

    #endregion

    #region Generate - Separator Tests

    [Test]
    public void Generate_EmptySeparator_ConcatenatesWords()
    {
        var passphrase = PassphraseGenerator.Generate(separator: "");

        // No separators, so should be all lowercase letters
        Assert.That(passphrase, Does.Match("^[a-z]+$"));
    }

    [Test]
    public void Generate_MultiCharSeparator_UsesFullSeparator()
    {
        var passphrase = PassphraseGenerator.Generate(wordCount: 5, separator: "---");

        var parts = passphrase.Split("---");
        Assert.That(parts.Length, Is.EqualTo(5));
    }

    [Test]
    public void Generate_UnderscoreSeparator_Works()
    {
        var passphrase = PassphraseGenerator.Generate(separator: "_");

        Assert.That(passphrase, Does.Contain("_"));
        Assert.That(passphrase, Does.Not.Contain("-"));
    }

    #endregion

    #region Generate - Word Count Tests

    [TestCase(5)]
    [TestCase(6)]
    [TestCase(7)]
    [TestCase(8)]
    [TestCase(10)]
    public void Generate_VariousWordCounts_ReturnsCorrectCount(int wordCount)
    {
        var passphrase = PassphraseGenerator.Generate(wordCount: wordCount);

        var words = passphrase.Split('-');
        Assert.That(words.Length, Is.EqualTo(wordCount));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    public void Generate_BelowMinimumWordCounts_ThrowsArgumentException(int wordCount)
    {
        Assert.Throws<ArgumentException>(() =>
            PassphraseGenerator.Generate(wordCount: wordCount));
    }

    #endregion

    #region CalculateEntropy - Tests

    [Test]
    public void CalculateEntropy_FiveWords_ReturnsApproximately65Bits()
    {
        var entropy = PassphraseGenerator.CalculateEntropy(5);

        // 5 words should give approximately 64-65 bits
        Assert.That(entropy, Is.GreaterThan(60));
        Assert.That(entropy, Is.LessThan(70));
    }

    [Test]
    public void CalculateEntropy_SixWords_ReturnsApproximately78Bits()
    {
        var entropy = PassphraseGenerator.CalculateEntropy(6);

        // 6 words should give approximately 77-78 bits
        Assert.That(entropy, Is.GreaterThan(75));
        Assert.That(entropy, Is.LessThan(85));
    }

    [Test]
    public void CalculateEntropy_SevenWords_ReturnsApproximately90Bits()
    {
        var entropy = PassphraseGenerator.CalculateEntropy(7);

        // 7 words should give approximately 90 bits
        Assert.That(entropy, Is.GreaterThan(85));
        Assert.That(entropy, Is.LessThan(100));
    }

    [Test]
    public void CalculateEntropy_MoreWords_HigherEntropy()
    {
        var entropy5 = PassphraseGenerator.CalculateEntropy(5);
        var entropy6 = PassphraseGenerator.CalculateEntropy(6);
        var entropy7 = PassphraseGenerator.CalculateEntropy(7);
        var entropy8 = PassphraseGenerator.CalculateEntropy(8);

        Assert.That(entropy6, Is.GreaterThan(entropy5));
        Assert.That(entropy7, Is.GreaterThan(entropy6));
        Assert.That(entropy8, Is.GreaterThan(entropy7));
    }

    [Test]
    public void CalculateEntropy_Zero_ReturnsZero()
    {
        var entropy = PassphraseGenerator.CalculateEntropy(0);

        Assert.That(entropy, Is.EqualTo(0));
    }

    [Test]
    public void CalculateEntropy_One_ReturnsPerWordEntropy()
    {
        var entropy = PassphraseGenerator.CalculateEntropy(1);

        // Should be approximately log2(wordlistSize) ≈ 12.9 bits
        Assert.That(entropy, Is.GreaterThan(12));
        Assert.That(entropy, Is.LessThan(14));
    }

    [Test]
    public void CalculateEntropy_ScalesLinearly()
    {
        var entropy1 = PassphraseGenerator.CalculateEntropy(1);
        var entropy2 = PassphraseGenerator.CalculateEntropy(2);

        // Entropy should double when word count doubles (approximately)
        Assert.That(entropy2, Is.EqualTo(entropy1 * 2).Within(0.001));
    }

    #endregion

    #region SecurityConstants Integration Tests

    [Test]
    public void SecurityConstants_MinimumPassphraseWords_Is5()
    {
        Assert.That(SecurityConstants.MinimumPassphraseWords, Is.EqualTo(5));
    }

    [Test]
    public void SecurityConstants_RecommendedPassphraseWords_Is6()
    {
        Assert.That(SecurityConstants.RecommendedPassphraseWords, Is.EqualTo(6));
    }

    [Test]
    public void SecurityConstants_MinimumWordlistSize_Is5000()
    {
        Assert.That(SecurityConstants.MinimumWordlistSize, Is.EqualTo(5000));
    }

    [Test]
    public void Generate_RecommendedWords_ProducesSecureEntropy()
    {
        var entropy = PassphraseGenerator.CalculateEntropy(SecurityConstants.RecommendedPassphraseWords);

        // 77.5 bits is target for recommended
        Assert.That(entropy, Is.GreaterThan(75));
    }

    [Test]
    public void Generate_MinimumWords_ProducesAcceptableEntropy()
    {
        var entropy = PassphraseGenerator.CalculateEntropy(SecurityConstants.MinimumPassphraseWords);

        // 64.6 bits is minimum acceptable
        Assert.That(entropy, Is.GreaterThan(60));
    }

    #endregion
}
