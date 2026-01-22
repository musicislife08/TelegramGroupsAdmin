using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.UnitTests.Core.Utilities;

/// <summary>
/// Unit tests for TextTokenizer.
/// Tests tokenization, word extraction, stop word filtering, and emoji removal
/// used in SimHash, Jaccard similarity, Bayes classification, and content detection.
/// </summary>
[TestFixture]
public class TextTokenizerTests
{
    #region TokenizeToSet - Basic Tests

    [Test]
    public void TokenizeToSet_SimpleWords_ReturnsSet()
    {
        var result = TextTokenizer.TokenizeToSet("hello world");

        Assert.That(result, Contains.Item("hello"));
        Assert.That(result, Contains.Item("world"));
        Assert.That(result.Count, Is.EqualTo(2));
    }

    [Test]
    public void TokenizeToSet_DuplicateWords_ReturnsUnique()
    {
        var result = TextTokenizer.TokenizeToSet("hello hello hello world world");

        Assert.That(result, Contains.Item("hello"));
        Assert.That(result, Contains.Item("world"));
        Assert.That(result.Count, Is.EqualTo(2));
    }

    [Test]
    public void TokenizeToSet_NullText_ReturnsEmpty()
    {
        var result = TextTokenizer.TokenizeToSet(null);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void TokenizeToSet_EmptyText_ReturnsEmpty()
    {
        var result = TextTokenizer.TokenizeToSet("");

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void TokenizeToSet_WhitespaceOnly_ReturnsEmpty()
    {
        var result = TextTokenizer.TokenizeToSet("   ");

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void TokenizeToSet_ConvertsToLowerCase()
    {
        var result = TextTokenizer.TokenizeToSet("Hello WORLD");

        Assert.That(result, Contains.Item("hello"));
        Assert.That(result, Contains.Item("world"));
        Assert.That(result, Does.Not.Contain("Hello"));
        Assert.That(result, Does.Not.Contain("WORLD"));
    }

    #endregion

    #region TokenizeToSet - Punctuation and Whitespace

    [Test]
    public void TokenizeToSet_SplitsOnPunctuation()
    {
        var result = TextTokenizer.TokenizeToSet("hello,world.test!foo?bar");

        Assert.That(result, Contains.Item("hello"));
        Assert.That(result, Contains.Item("world"));
        Assert.That(result, Contains.Item("test"));
        Assert.That(result, Contains.Item("foo"));
        Assert.That(result, Contains.Item("bar"));
    }

    [Test]
    public void TokenizeToSet_SplitsOnMultipleSpaces()
    {
        var result = TextTokenizer.TokenizeToSet("hello   world    test");

        Assert.That(result, Contains.Item("hello"));
        Assert.That(result, Contains.Item("world"));
        Assert.That(result, Contains.Item("test"));
    }

    [Test]
    public void TokenizeToSet_SplitsOnTabs()
    {
        var result = TextTokenizer.TokenizeToSet("hello\tworld");

        Assert.That(result, Contains.Item("hello"));
        Assert.That(result, Contains.Item("world"));
    }

    [Test]
    public void TokenizeToSet_SplitsOnNewlines()
    {
        var result = TextTokenizer.TokenizeToSet("hello\nworld\r\ntest");

        Assert.That(result, Contains.Item("hello"));
        Assert.That(result, Contains.Item("world"));
        Assert.That(result, Contains.Item("test"));
    }

    #endregion

    #region TokenizeToSet - Minimum Length Filtering

    [Test]
    public void TokenizeToSet_FiltersSingleCharacters_ByDefault()
    {
        var result = TextTokenizer.TokenizeToSet("a b c hello world");

        Assert.That(result, Does.Not.Contain("a"));
        Assert.That(result, Does.Not.Contain("b"));
        Assert.That(result, Does.Not.Contain("c"));
        Assert.That(result, Contains.Item("hello"));
        Assert.That(result, Contains.Item("world"));
    }

    [Test]
    public void TokenizeToSet_CustomMinLength_FiltersAccordingly()
    {
        var result = TextTokenizer.TokenizeToSet("a ab abc abcd hello", minLength: 4);

        Assert.That(result, Does.Not.Contain("a"));
        Assert.That(result, Does.Not.Contain("ab"));
        Assert.That(result, Does.Not.Contain("abc"));
        Assert.That(result, Contains.Item("abcd"));
        Assert.That(result, Contains.Item("hello"));
    }

    [Test]
    public void TokenizeToSet_MinLengthOne_IncludesAll()
    {
        var result = TextTokenizer.TokenizeToSet("a b c", minLength: 1);

        Assert.That(result, Contains.Item("a"));
        Assert.That(result, Contains.Item("b"));
        Assert.That(result, Contains.Item("c"));
    }

    #endregion

    #region TokenizeToArray - Basic Tests

    [Test]
    public void TokenizeToArray_SimpleWords_ReturnsArray()
    {
        var result = TextTokenizer.TokenizeToArray("hello world");

        Assert.That(result, Contains.Item("hello"));
        Assert.That(result, Contains.Item("world"));
        Assert.That(result.Length, Is.EqualTo(2));
    }

    [Test]
    public void TokenizeToArray_DuplicateWords_PreservesDuplicates()
    {
        var result = TextTokenizer.TokenizeToArray("hello hello world");

        Assert.That(result.Count(x => x == "hello"), Is.EqualTo(2));
        Assert.That(result.Count(x => x == "world"), Is.EqualTo(1));
        Assert.That(result.Length, Is.EqualTo(3));
    }

    [Test]
    public void TokenizeToArray_NullText_ReturnsEmpty()
    {
        var result = TextTokenizer.TokenizeToArray(null);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void TokenizeToArray_EmptyText_ReturnsEmpty()
    {
        var result = TextTokenizer.TokenizeToArray("");

        Assert.That(result, Is.Empty);
    }

    #endregion

    #region ExtractWords - Basic Tests

    [Test]
    public void ExtractWords_SimpleText_ExtractsWords()
    {
        var result = TextTokenizer.ExtractWords("Hello world test");

        Assert.That(result, Contains.Item("hello"));
        Assert.That(result, Contains.Item("world"));
        Assert.That(result, Contains.Item("test"));
    }

    [Test]
    public void ExtractWords_NullText_ReturnsEmpty()
    {
        var result = TextTokenizer.ExtractWords(null);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void ExtractWords_EmptyText_ReturnsEmpty()
    {
        var result = TextTokenizer.ExtractWords("");

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void ExtractWords_WhitespaceOnly_ReturnsEmpty()
    {
        var result = TextTokenizer.ExtractWords("   ");

        Assert.That(result, Is.Empty);
    }

    #endregion

    #region ExtractWords - Stop Word Filtering

    [Test]
    public void ExtractWords_DefaultOptions_RemovesStopWords()
    {
        var result = TextTokenizer.ExtractWords("the quick brown fox");

        Assert.That(result, Does.Not.Contain("the"));
        Assert.That(result, Contains.Item("quick"));
        Assert.That(result, Contains.Item("brown"));
        Assert.That(result, Contains.Item("fox"));
    }

    [Test]
    public void ExtractWords_WithStopWordsDisabled_KeepsStopWords()
    {
        var options = new TokenizerOptions { RemoveStopWords = false };
        var result = TextTokenizer.ExtractWords("the quick brown fox", options);

        Assert.That(result, Contains.Item("the"));
        Assert.That(result, Contains.Item("quick"));
        Assert.That(result, Contains.Item("brown"));
        Assert.That(result, Contains.Item("fox"));
    }

    [Test]
    public void ExtractWords_AllStopWords_ReturnsEmpty()
    {
        var result = TextTokenizer.ExtractWords("the and for are");

        Assert.That(result, Is.Empty);
    }

    #endregion

    #region ExtractWords - Number Filtering

    [Test]
    public void ExtractWords_DefaultOptions_RemovesNumbers()
    {
        var result = TextTokenizer.ExtractWords("test 123 hello 456");

        Assert.That(result, Does.Not.Contain("123"));
        Assert.That(result, Does.Not.Contain("456"));
        Assert.That(result, Contains.Item("test"));
        Assert.That(result, Contains.Item("hello"));
    }

    [Test]
    public void ExtractWords_WithNumbersDisabled_KeepsNumbers()
    {
        var options = new TokenizerOptions { RemoveNumbers = false };
        var result = TextTokenizer.ExtractWords("test 123 hello 456", options);

        Assert.That(result, Contains.Item("123"));
        Assert.That(result, Contains.Item("456"));
    }

    [Test]
    public void ExtractWords_MixedAlphanumeric_KeepsMixed()
    {
        var result = TextTokenizer.ExtractWords("test123 abc456 hello");

        // "test123" and "abc456" are not pure numbers, should be kept
        Assert.That(result, Contains.Item("test123"));
        Assert.That(result, Contains.Item("abc456"));
        Assert.That(result, Contains.Item("hello"));
    }

    #endregion

    #region ExtractWords - Minimum Length

    [Test]
    public void ExtractWords_DefaultMinLength_FiltersShort()
    {
        var result = TextTokenizer.ExtractWords("a ab abc abcd hello");

        Assert.That(result, Does.Not.Contain("a"));
        Assert.That(result, Contains.Item("ab"));
        Assert.That(result, Contains.Item("abc"));
        Assert.That(result, Contains.Item("abcd"));
        Assert.That(result, Contains.Item("hello"));
    }

    [Test]
    public void ExtractWords_CustomMinLength_FiltersAccordingly()
    {
        var options = new TokenizerOptions { MinWordLength = 4 };
        var result = TextTokenizer.ExtractWords("a ab abc abcd hello", options);

        Assert.That(result, Does.Not.Contain("a"));
        Assert.That(result, Does.Not.Contain("ab"));
        Assert.That(result, Does.Not.Contain("abc"));
        Assert.That(result, Contains.Item("abcd"));
        Assert.That(result, Contains.Item("hello"));
    }

    #endregion

    #region ExtractWords - Case Handling

    [Test]
    public void ExtractWords_DefaultOptions_ConvertsToLowerCase()
    {
        var result = TextTokenizer.ExtractWords("Hello WORLD Test");

        Assert.That(result, Contains.Item("hello"));
        Assert.That(result, Contains.Item("world"));
        Assert.That(result, Contains.Item("test"));
        Assert.That(result, Does.Not.Contain("Hello"));
        Assert.That(result, Does.Not.Contain("WORLD"));
    }

    [Test]
    public void ExtractWords_WithLowerCaseDisabled_PreservesCase()
    {
        var options = new TokenizerOptions { ConvertToLowerCase = false };
        var result = TextTokenizer.ExtractWords("Hello WORLD Test", options);

        Assert.That(result, Contains.Item("Hello"));
        Assert.That(result, Contains.Item("WORLD"));
        Assert.That(result, Contains.Item("Test"));
    }

    #endregion

    #region RemoveEmojis - Basic Tests

    [Test]
    public void RemoveEmojis_TextWithoutEmojis_ReturnsOriginal()
    {
        var result = TextTokenizer.RemoveEmojis("Hello world");

        Assert.That(result, Is.EqualTo("Hello world"));
    }

    [Test]
    public void RemoveEmojis_NullText_ReturnsNull()
    {
        var result = TextTokenizer.RemoveEmojis(null!);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void RemoveEmojis_EmptyText_ReturnsEmpty()
    {
        var result = TextTokenizer.RemoveEmojis("");

        Assert.That(result, Is.EqualTo(""));
    }

    [Test]
    public void RemoveEmojis_WithDingbatEmojis_RemovesThem()
    {
        // ✓ (U+2713) is in the 2700-27BF range
        var result = TextTokenizer.RemoveEmojis("Hello ✓ world");

        Assert.That(result, Does.Not.Contain("✓"));
        Assert.That(result, Does.Contain("Hello"));
        Assert.That(result, Does.Contain("world"));
    }

    [Test]
    public void RemoveEmojis_WithMiscSymbols_RemovesThem()
    {
        // ☀ (U+2600) is in the 2600-26FF range
        var result = TextTokenizer.RemoveEmojis("Hello ☀ world");

        Assert.That(result, Does.Not.Contain("☀"));
        Assert.That(result, Does.Contain("Hello"));
        Assert.That(result, Does.Contain("world"));
    }

    [Test]
    public void RemoveEmojis_CollapsesMultipleSpaces()
    {
        // After removing emojis, multiple spaces should be collapsed
        var result = TextTokenizer.RemoveEmojis("Hello ☀ ☀ world");

        // Should not have multiple consecutive spaces
        Assert.That(result, Does.Not.Contain("  "));
    }

    #endregion

    #region IsStopWord - Tests

    [Test]
    public void IsStopWord_CommonStopWord_ReturnsTrue()
    {
        Assert.That(TextTokenizer.IsStopWord("the"), Is.True);
        Assert.That(TextTokenizer.IsStopWord("and"), Is.True);
        Assert.That(TextTokenizer.IsStopWord("for"), Is.True);
        Assert.That(TextTokenizer.IsStopWord("are"), Is.True);
    }

    [Test]
    public void IsStopWord_ContentWord_ReturnsFalse()
    {
        Assert.That(TextTokenizer.IsStopWord("hello"), Is.False);
        Assert.That(TextTokenizer.IsStopWord("world"), Is.False);
        Assert.That(TextTokenizer.IsStopWord("test"), Is.False);
        Assert.That(TextTokenizer.IsStopWord("example"), Is.False);
    }

    [Test]
    public void IsStopWord_CaseInsensitive()
    {
        Assert.That(TextTokenizer.IsStopWord("THE"), Is.True);
        Assert.That(TextTokenizer.IsStopWord("The"), Is.True);
        Assert.That(TextTokenizer.IsStopWord("thE"), Is.True);
    }

    [Test]
    public void IsStopWord_EmptyString_ReturnsFalse()
    {
        Assert.That(TextTokenizer.IsStopWord(""), Is.False);
    }

    #endregion

    #region GetWordFrequencies - Tests

    [Test]
    public void GetWordFrequencies_SimpleText_ReturnsFrequencies()
    {
        var result = TextTokenizer.GetWordFrequencies("hello world hello");

        Assert.That(result["hello"], Is.EqualTo(2));
        Assert.That(result["world"], Is.EqualTo(1));
    }

    [Test]
    public void GetWordFrequencies_NullText_ReturnsEmpty()
    {
        var result = TextTokenizer.GetWordFrequencies(null);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetWordFrequencies_EmptyText_ReturnsEmpty()
    {
        var result = TextTokenizer.GetWordFrequencies("");

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetWordFrequencies_RemovesStopWords_ByDefault()
    {
        var result = TextTokenizer.GetWordFrequencies("the quick brown fox");

        Assert.That(result.ContainsKey("the"), Is.False);
        Assert.That(result.ContainsKey("quick"), Is.True);
    }

    [Test]
    public void GetWordFrequencies_WithOptions_AppliesFiltering()
    {
        var options = new TokenizerOptions { RemoveStopWords = false };
        var result = TextTokenizer.GetWordFrequencies("the the the quick", options);

        Assert.That(result["the"], Is.EqualTo(3));
        Assert.That(result["quick"], Is.EqualTo(1));
    }

    #endregion

    #region StopWords Collection - Tests

    [Test]
    public void StopWords_ContainsExpectedWords()
    {
        Assert.That(TextTokenizer.StopWords, Contains.Item("the"));
        Assert.That(TextTokenizer.StopWords, Contains.Item("and"));
        Assert.That(TextTokenizer.StopWords, Contains.Item("for"));
        Assert.That(TextTokenizer.StopWords, Contains.Item("with"));
        Assert.That(TextTokenizer.StopWords, Contains.Item("this"));
        Assert.That(TextTokenizer.StopWords, Contains.Item("would"));
    }

    [Test]
    public void StopWords_DoesNotContainContentWords()
    {
        Assert.That(TextTokenizer.StopWords, Does.Not.Contain("hello"));
        Assert.That(TextTokenizer.StopWords, Does.Not.Contain("world"));
        Assert.That(TextTokenizer.StopWords, Does.Not.Contain("computer"));
        Assert.That(TextTokenizer.StopWords, Does.Not.Contain("example"));
    }

    [Test]
    public void StopWords_IsCaseInsensitive()
    {
        // The HashSet uses OrdinalIgnoreCase comparer
        Assert.That(TextTokenizer.StopWords.Contains("THE"), Is.True);
        Assert.That(TextTokenizer.StopWords.Contains("The"), Is.True);
        Assert.That(TextTokenizer.StopWords.Contains("tHe"), Is.True);
    }

    #endregion

    #region TokenizerOptions - Tests

    [Test]
    public void TokenizerOptions_Default_HasExpectedValues()
    {
        var options = TokenizerOptions.Default;

        Assert.That(options.RemoveEmojis, Is.True);
        Assert.That(options.RemoveStopWords, Is.True);
        Assert.That(options.RemoveNumbers, Is.True);
        Assert.That(options.MinWordLength, Is.EqualTo(2));
        Assert.That(options.ConvertToLowerCase, Is.True);
    }

    [Test]
    public void TokenizerOptions_CustomInstance_CanOverrideDefaults()
    {
        var options = new TokenizerOptions
        {
            RemoveEmojis = false,
            RemoveStopWords = false,
            RemoveNumbers = false,
            MinWordLength = 5,
            ConvertToLowerCase = false
        };

        Assert.That(options.RemoveEmojis, Is.False);
        Assert.That(options.RemoveStopWords, Is.False);
        Assert.That(options.RemoveNumbers, Is.False);
        Assert.That(options.MinWordLength, Is.EqualTo(5));
        Assert.That(options.ConvertToLowerCase, Is.False);
    }

    #endregion

    #region Integration Tests

    [Test]
    public void ExtractWords_RealSpamText_ExtractsContentWords()
    {
        var spamText = "Check out this AMAZING offer! Click here now for FREE money!!!";
        var result = TextTokenizer.ExtractWords(spamText);

        Assert.That(result, Contains.Item("check"));
        Assert.That(result, Contains.Item("amazing"));
        Assert.That(result, Contains.Item("offer"));
        Assert.That(result, Contains.Item("click"));
        Assert.That(result, Contains.Item("free"));
        Assert.That(result, Contains.Item("money"));
        // Stop words should be removed
        Assert.That(result, Does.Not.Contain("this"));
        Assert.That(result, Does.Not.Contain("for"));
    }

    [Test]
    public void TokenizeToSet_RealMessageText_TokenizesCorrectly()
    {
        var messageText = "Hello everyone! How are you doing today?";
        var result = TextTokenizer.TokenizeToSet(messageText);

        Assert.That(result, Contains.Item("hello"));
        Assert.That(result, Contains.Item("everyone"));
        Assert.That(result, Contains.Item("how"));
        Assert.That(result, Contains.Item("are"));
        Assert.That(result, Contains.Item("you"));
        Assert.That(result, Contains.Item("doing"));
        Assert.That(result, Contains.Item("today"));
    }

    [Test]
    public void ExtractWords_WithApostrophes_KeepsContractions()
    {
        var text = "I'm can't won't don't";
        var options = new TokenizerOptions { RemoveStopWords = false };
        var result = TextTokenizer.ExtractWords(text, options);

        // Word boundary regex includes apostrophes
        Assert.That(result, Contains.Item("i'm"));
        Assert.That(result, Contains.Item("can't"));
        Assert.That(result, Contains.Item("won't"));
        Assert.That(result, Contains.Item("don't"));
    }

    #endregion
}
