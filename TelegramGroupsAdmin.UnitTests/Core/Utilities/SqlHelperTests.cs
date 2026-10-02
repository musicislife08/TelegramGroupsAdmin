using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.UnitTests.Core.Utilities;

/// <summary>
/// Unit tests for SqlHelper.
/// Locks down the identifier-quoting contract used to build dynamic SQL in the backup
/// system, where PostgreSQL cannot parameterize table, column or constraint names.
/// </summary>
[TestFixture]
public class SqlHelperTests
{
    #region QuoteIdentifier - Plain Identifiers

    [Test]
    public void QuoteIdentifier_SnakeCaseIdentifier_WrapsInDoubleQuotes()
    {
        var result = SqlHelper.QuoteIdentifier("user_name");

        Assert.That(result, Is.EqualTo("\"user_name\""));
    }

    [TestCase("users")]
    [TestCase("telegram_users")]
    [TestCase("PK_messages")]
    [TestCase("column1")]
    public void QuoteIdentifier_NoSpecialCharacters_PassesThroughUnchangedInsideQuotes(string identifier)
    {
        var result = SqlHelper.QuoteIdentifier(identifier);

        // Strip exactly one wrapping quote from each end; the body must be the input, untouched
        Assert.That(result[1..^1], Is.EqualTo(identifier));
    }

    [Test]
    public void QuoteIdentifier_MixedCaseIdentifier_PreservesCase()
    {
        // Quoting is what makes PostgreSQL keep the case, so the helper must not fold it
        var result = SqlHelper.QuoteIdentifier("FK_Messages_Users");

        Assert.That(result, Is.EqualTo("\"FK_Messages_Users\""));
    }

    #endregion

    #region QuoteIdentifier - Wrapping

    [TestCase("x")]
    [TestCase("order by")]
    [TestCase("trailing\"")]
    [TestCase("\"leading")]
    [TestCase("таблица")]
    public void QuoteIdentifier_AnyAcceptedInput_StartsAndEndsWithDoubleQuote(string identifier)
    {
        var result = SqlHelper.QuoteIdentifier(identifier);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Does.StartWith("\""));
            Assert.That(result, Does.EndWith("\""));
        }
    }

    #endregion

    #region QuoteIdentifier - Embedded Double Quotes

    [Test]
    public void QuoteIdentifier_EmbeddedDoubleQuote_DoublesIt()
    {
        var result = SqlHelper.QuoteIdentifier("col\"name");

        Assert.That(result, Is.EqualTo("\"col\"\"name\""));
    }

    [Test]
    public void QuoteIdentifier_MultipleEmbeddedDoubleQuotes_DoublesEachOne()
    {
        var result = SqlHelper.QuoteIdentifier("a\"b\"c");

        Assert.That(result, Is.EqualTo("\"a\"\"b\"\"c\""));
    }

    [Test]
    public void QuoteIdentifier_OnlyADoubleQuote_ProducesFourQuotes()
    {
        // One opening quote, the escaped pair, one closing quote
        var result = SqlHelper.QuoteIdentifier("\"");

        Assert.That(result, Is.EqualTo("\"\"\"\""));
    }

    [Test]
    public void QuoteIdentifier_ConsecutiveDoubleQuotes_DoublesEachOneIndependently()
    {
        // Two quotes in: four escaped quotes inside the wrapper, six characters in total
        var result = SqlHelper.QuoteIdentifier("\"\"");

        Assert.That(result, Is.EqualTo("\"\"\"\"\"\""));
    }

    [Test]
    public void QuoteIdentifier_AlreadyQuotedInput_IsQuotedAgain()
    {
        // No "looks quoted already" shortcut: the surrounding quotes are data and get escaped
        var result = SqlHelper.QuoteIdentifier("\"users\"");

        Assert.That(result, Is.EqualTo("\"\"\"users\"\"\""));
    }

    [Test]
    public void QuoteIdentifier_AlreadyQuotedInjectionAttempt_IsEscapedNotPassedThrough()
    {
        // Starts and ends with a quote, so a pass-through shortcut would return it verbatim
        var result = SqlHelper.QuoteIdentifier("\"a\"; DROP TABLE users; --\"");

        Assert.That(result, Is.EqualTo("\"\"\"a\"\"; DROP TABLE users; --\"\"\""));
    }

    [Test]
    public void QuoteIdentifier_InjectionAttempt_CannotCloseTheIdentifierEarly()
    {
        const string malicious = "users\"; DROP TABLE users; --";

        var result = SqlHelper.QuoteIdentifier(malicious);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo("\"users\"\"; DROP TABLE users; --\""));

            // Structural check, independent of the exact string above: every quote inside the wrapper
            // is part of a doubled pair, so none can terminate the identifier
            var body = result[1..^1];
            Assert.That(body.Replace("\"\"", string.Empty), Does.Not.Contain("\""));
        }
    }

    #endregion

    #region QuoteIdentifier - Accepted Edge Cases

    [Test]
    public void QuoteIdentifier_WhitespaceOnly_IsAcceptedAndQuotedAsIs()
    {
        // ThrowIfNullOrEmpty rejects only null and empty; whitespace is a legal quoted identifier
        var result = SqlHelper.QuoteIdentifier("  ");

        Assert.That(result, Is.EqualTo("\"  \""));
    }

    [TestCase("two words", "\"two words\"")]
    [TestCase("a;b", "\"a;b\"")]
    [TestCase("it's", "\"it's\"")]
    [TestCase("back\\slash", "\"back\\slash\"")]
    [TestCase("таблица", "\"таблица\"")]
    public void QuoteIdentifier_CharactersOtherThanDoubleQuote_AreNotEscaped(string identifier, string expected)
    {
        // Only the double quote is special inside a quoted identifier; everything else is literal
        var result = SqlHelper.QuoteIdentifier(identifier);

        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void QuoteIdentifier_ReservedKeyword_IsQuotedLikeAnyOtherIdentifier()
    {
        var result = SqlHelper.QuoteIdentifier("select");

        Assert.That(result, Is.EqualTo("\"select\""));
    }

    #endregion

    #region QuoteIdentifier - Input Validation

    [Test]
    public void QuoteIdentifier_Null_ThrowsArgumentNullException()
    {
        Assert.That(() => SqlHelper.QuoteIdentifier(null!), Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void QuoteIdentifier_EmptyString_ThrowsArgumentException()
    {
        // TypeOf, not InstanceOf: empty must throw ArgumentException itself, not the null subtype
        Assert.That(() => SqlHelper.QuoteIdentifier(string.Empty), Throws.TypeOf<ArgumentException>());
    }

    #endregion
}
