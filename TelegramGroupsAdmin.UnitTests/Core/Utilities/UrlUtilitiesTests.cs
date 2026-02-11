using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.UnitTests.Core.Utilities;

/// <summary>
/// Unit tests for UrlUtilities.
/// Tests URL extraction from text using regex pattern matching.
/// Used in URL filtering, link detection, and content analysis.
/// </summary>
[TestFixture]
public class UrlUtilitiesTests
{
    #region ExtractUrls - Basic Tests

    [Test]
    public void ExtractUrls_SingleHttpUrl_ExtractsCorrectly()
    {
        var result = UrlUtilities.ExtractUrls("Check out http://example.com for more info");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result![0], Is.EqualTo("http://example.com"));
    }

    [Test]
    public void ExtractUrls_SingleHttpsUrl_ExtractsCorrectly()
    {
        var result = UrlUtilities.ExtractUrls("Visit https://example.com for details");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result![0], Is.EqualTo("https://example.com"));
    }

    [Test]
    public void ExtractUrls_MultipleUrls_ExtractsAll()
    {
        var result = UrlUtilities.ExtractUrls("Check http://one.com and https://two.com");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result, Contains.Item("http://one.com"));
        Assert.That(result, Contains.Item("https://two.com"));
    }

    [Test]
    public void ExtractUrls_NoUrls_ReturnsNull()
    {
        var result = UrlUtilities.ExtractUrls("This text has no URLs in it");

        Assert.That(result, Is.Null);
    }

    [Test]
    public void ExtractUrls_NullText_ReturnsNull()
    {
        var result = UrlUtilities.ExtractUrls(null);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void ExtractUrls_EmptyText_ReturnsNull()
    {
        var result = UrlUtilities.ExtractUrls("");

        Assert.That(result, Is.Null);
    }

    [Test]
    public void ExtractUrls_WhitespaceOnly_ReturnsNull()
    {
        var result = UrlUtilities.ExtractUrls("   ");

        Assert.That(result, Is.Null);
    }

    #endregion

    #region ExtractUrls - URL Formats

    [Test]
    public void ExtractUrls_UrlWithPath_ExtractsFullPath()
    {
        var result = UrlUtilities.ExtractUrls("See https://example.com/path/to/page");

        Assert.That(result, Is.Not.Null);
        Assert.That(result![0], Is.EqualTo("https://example.com/path/to/page"));
    }

    [Test]
    public void ExtractUrls_UrlWithQueryParams_ExtractsWithParams()
    {
        var result = UrlUtilities.ExtractUrls("Link: https://example.com/page?id=123&name=test");

        Assert.That(result, Is.Not.Null);
        Assert.That(result![0], Is.EqualTo("https://example.com/page?id=123&name=test"));
    }

    [Test]
    public void ExtractUrls_UrlWithFragment_ExtractsWithFragment()
    {
        var result = UrlUtilities.ExtractUrls("Go to https://example.com/page#section");

        Assert.That(result, Is.Not.Null);
        Assert.That(result![0], Is.EqualTo("https://example.com/page#section"));
    }

    [Test]
    public void ExtractUrls_UrlWithPort_ExtractsWithPort()
    {
        var result = UrlUtilities.ExtractUrls("Server at https://example.com:8080/api");

        Assert.That(result, Is.Not.Null);
        Assert.That(result![0], Is.EqualTo("https://example.com:8080/api"));
    }

    [Test]
    public void ExtractUrls_UrlWithSubdomain_ExtractsCorrectly()
    {
        var result = UrlUtilities.ExtractUrls("Visit https://www.example.com");

        Assert.That(result, Is.Not.Null);
        Assert.That(result![0], Is.EqualTo("https://www.example.com"));
    }

    [Test]
    public void ExtractUrls_UrlWithIpAddress_ExtractsCorrectly()
    {
        var result = UrlUtilities.ExtractUrls("API at http://192.168.1.1/api");

        Assert.That(result, Is.Not.Null);
        Assert.That(result![0], Is.EqualTo("http://192.168.1.1/api"));
    }

    #endregion

    #region ExtractUrls - Case Insensitivity

    [Test]
    public void ExtractUrls_UppercaseHttp_ExtractsCorrectly()
    {
        var result = UrlUtilities.ExtractUrls("Check HTTP://EXAMPLE.COM");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Count.EqualTo(1));
    }

    [Test]
    public void ExtractUrls_UppercaseHttps_ExtractsCorrectly()
    {
        var result = UrlUtilities.ExtractUrls("Check HTTPS://EXAMPLE.COM");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Count.EqualTo(1));
    }

    [Test]
    public void ExtractUrls_MixedCase_ExtractsCorrectly()
    {
        var result = UrlUtilities.ExtractUrls("Check HtTpS://Example.Com/Page");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Count.EqualTo(1));
    }

    #endregion

    #region ExtractUrls - Boundary Handling

    [Test]
    public void ExtractUrls_UrlInParentheses_StopsAtClosingParen()
    {
        var result = UrlUtilities.ExtractUrls("Link (https://example.com) here");

        Assert.That(result, Is.Not.Null);
        Assert.That(result![0], Is.EqualTo("https://example.com"));
    }

    [Test]
    public void ExtractUrls_UrlInBrackets_StopsAtClosingBracket()
    {
        var result = UrlUtilities.ExtractUrls("See [https://example.com] for more");

        Assert.That(result, Is.Not.Null);
        Assert.That(result![0], Is.EqualTo("https://example.com"));
    }

    [Test]
    public void ExtractUrls_UrlInAngleBrackets_StopsAtClosingBracket()
    {
        var result = UrlUtilities.ExtractUrls("Visit <https://example.com> now");

        Assert.That(result, Is.Not.Null);
        Assert.That(result![0], Is.EqualTo("https://example.com"));
    }

    [Test]
    public void ExtractUrls_UrlAtEndOfSentence_StopsAtWhitespace()
    {
        var result = UrlUtilities.ExtractUrls("Visit https://example.com today");

        Assert.That(result, Is.Not.Null);
        Assert.That(result![0], Is.EqualTo("https://example.com"));
    }

    [Test]
    public void ExtractUrls_UrlFollowedByNewline_StopsAtNewline()
    {
        var result = UrlUtilities.ExtractUrls("Visit https://example.com\nMore text");

        Assert.That(result, Is.Not.Null);
        Assert.That(result![0], Is.EqualTo("https://example.com"));
    }

    #endregion

    #region ExtractUrls - Edge Cases

    [Test]
    public void ExtractUrls_OnlyUrl_ExtractsCorrectly()
    {
        var result = UrlUtilities.ExtractUrls("https://example.com");

        Assert.That(result, Is.Not.Null);
        Assert.That(result![0], Is.EqualTo("https://example.com"));
    }

    [Test]
    public void ExtractUrls_UrlAtStart_ExtractsCorrectly()
    {
        var result = UrlUtilities.ExtractUrls("https://example.com is the website");

        Assert.That(result, Is.Not.Null);
        Assert.That(result![0], Is.EqualTo("https://example.com"));
    }

    [Test]
    public void ExtractUrls_UrlAtEnd_ExtractsCorrectly()
    {
        var result = UrlUtilities.ExtractUrls("The website is https://example.com");

        Assert.That(result, Is.Not.Null);
        Assert.That(result![0], Is.EqualTo("https://example.com"));
    }

    [Test]
    public void ExtractUrls_ConsecutiveUrls_ExtractsBoth()
    {
        var result = UrlUtilities.ExtractUrls("https://one.com https://two.com");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Count.EqualTo(2));
    }

    [Test]
    public void ExtractUrls_DuplicateUrls_ExtractsBoth()
    {
        var result = UrlUtilities.ExtractUrls("Visit https://example.com and also https://example.com");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Count.EqualTo(2));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result![0], Is.EqualTo("https://example.com"));
            Assert.That(result[1], Is.EqualTo("https://example.com"));
        }
    }

    #endregion

    #region ExtractUrls - Non-URL Patterns

    [Test]
    public void ExtractUrls_FtpUrl_DoesNotMatch()
    {
        var result = UrlUtilities.ExtractUrls("File at ftp://example.com/file.txt");

        Assert.That(result, Is.Null);
    }

    [Test]
    public void ExtractUrls_MailtoLink_DoesNotMatch()
    {
        var result = UrlUtilities.ExtractUrls("Email mailto:test@example.com");

        Assert.That(result, Is.Null);
    }

    [Test]
    public void ExtractUrls_TelLink_DoesNotMatch()
    {
        var result = UrlUtilities.ExtractUrls("Call tel:+1234567890");

        Assert.That(result, Is.Null);
    }

    [Test]
    public void ExtractUrls_DomainWithoutProtocol_DoesNotMatch()
    {
        var result = UrlUtilities.ExtractUrls("Visit example.com for more");

        Assert.That(result, Is.Null);
    }

    [Test]
    public void ExtractUrls_PartialHttp_DoesNotMatch()
    {
        var result = UrlUtilities.ExtractUrls("http not a url");

        Assert.That(result, Is.Null);
    }

    #endregion

    #region ExtractUrls - Real-World Spam Patterns

    [Test]
    public void ExtractUrls_TypicalSpamMessage_ExtractsUrls()
    {
        var spamText = "🔥 HOT DEAL! Click https://scam-site.com/offer?id=12345 NOW! " +
                      "Or visit http://another-scam.net/promo for AMAZING prizes!";

        var result = UrlUtilities.ExtractUrls(spamText);

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result, Contains.Item("https://scam-site.com/offer?id=12345"));
        Assert.That(result, Contains.Item("http://another-scam.net/promo"));
    }

    [Test]
    public void ExtractUrls_TelegramGroupInvite_ExtractsUrl()
    {
        var inviteText = "Join our group: https://t.me/joinchat/ABCDEFG123456";

        var result = UrlUtilities.ExtractUrls(inviteText);

        Assert.That(result, Is.Not.Null);
        Assert.That(result![0], Is.EqualTo("https://t.me/joinchat/ABCDEFG123456"));
    }

    [Test]
    public void ExtractUrls_ShortenedUrl_ExtractsUrl()
    {
        var text = "Check this: https://bit.ly/abc123";

        var result = UrlUtilities.ExtractUrls(text);

        Assert.That(result, Is.Not.Null);
        Assert.That(result![0], Is.EqualTo("https://bit.ly/abc123"));
    }

    #endregion
}
