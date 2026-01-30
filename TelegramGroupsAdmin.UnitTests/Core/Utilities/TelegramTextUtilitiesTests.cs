using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.UnitTests.Core.Utilities;

/// <summary>
/// Unit tests for TelegramTextUtilities.
/// Tests MarkdownV2 escaping for Telegram messages.
/// </summary>
[TestFixture]
public class TelegramTextUtilitiesTests
{
    #region EscapeMarkdownV2 - Null and Empty Handling

    [Test]
    public void EscapeMarkdownV2_NullInput_ReturnsNull()
    {
        var result = TelegramTextUtilities.EscapeMarkdownV2(null!);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void EscapeMarkdownV2_EmptyString_ReturnsEmpty()
    {
        var result = TelegramTextUtilities.EscapeMarkdownV2("");

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void EscapeMarkdownV2_WhitespaceOnly_ReturnsUnchanged()
    {
        var result = TelegramTextUtilities.EscapeMarkdownV2("   ");

        Assert.That(result, Is.EqualTo("   "));
    }

    #endregion

    #region EscapeMarkdownV2 - No Special Characters

    [Test]
    public void EscapeMarkdownV2_PlainText_ReturnsUnchanged()
    {
        var result = TelegramTextUtilities.EscapeMarkdownV2("Hello world");

        Assert.That(result, Is.EqualTo("Hello world"));
    }

    [Test]
    public void EscapeMarkdownV2_AlphanumericOnly_ReturnsUnchanged()
    {
        var result = TelegramTextUtilities.EscapeMarkdownV2("Test123ABC");

        Assert.That(result, Is.EqualTo("Test123ABC"));
    }

    #endregion

    #region EscapeMarkdownV2 - Individual Special Characters

    [Test]
    public void EscapeMarkdownV2_Underscore_Escaped()
    {
        var result = TelegramTextUtilities.EscapeMarkdownV2("hello_world");

        Assert.That(result, Is.EqualTo("hello\\_world"));
    }

    [Test]
    public void EscapeMarkdownV2_Asterisk_Escaped()
    {
        var result = TelegramTextUtilities.EscapeMarkdownV2("*bold*");

        Assert.That(result, Is.EqualTo("\\*bold\\*"));
    }

    [Test]
    public void EscapeMarkdownV2_SquareBrackets_Escaped()
    {
        var result = TelegramTextUtilities.EscapeMarkdownV2("[link]");

        Assert.That(result, Is.EqualTo("\\[link\\]"));
    }

    [Test]
    public void EscapeMarkdownV2_Parentheses_Escaped()
    {
        var result = TelegramTextUtilities.EscapeMarkdownV2("(url)");

        Assert.That(result, Is.EqualTo("\\(url\\)"));
    }

    [Test]
    public void EscapeMarkdownV2_Tilde_Escaped()
    {
        var result = TelegramTextUtilities.EscapeMarkdownV2("~strikethrough~");

        Assert.That(result, Is.EqualTo("\\~strikethrough\\~"));
    }

    [Test]
    public void EscapeMarkdownV2_Backtick_Escaped()
    {
        var result = TelegramTextUtilities.EscapeMarkdownV2("`code`");

        Assert.That(result, Is.EqualTo("\\`code\\`"));
    }

    [Test]
    public void EscapeMarkdownV2_GreaterThan_Escaped()
    {
        var result = TelegramTextUtilities.EscapeMarkdownV2(">quote");

        Assert.That(result, Is.EqualTo("\\>quote"));
    }

    [Test]
    public void EscapeMarkdownV2_Hash_Escaped()
    {
        var result = TelegramTextUtilities.EscapeMarkdownV2("#hashtag");

        Assert.That(result, Is.EqualTo("\\#hashtag"));
    }

    [Test]
    public void EscapeMarkdownV2_Plus_Escaped()
    {
        var result = TelegramTextUtilities.EscapeMarkdownV2("a+b");

        Assert.That(result, Is.EqualTo("a\\+b"));
    }

    [Test]
    public void EscapeMarkdownV2_Minus_Escaped()
    {
        var result = TelegramTextUtilities.EscapeMarkdownV2("a-b");

        Assert.That(result, Is.EqualTo("a\\-b"));
    }

    [Test]
    public void EscapeMarkdownV2_Equals_Escaped()
    {
        var result = TelegramTextUtilities.EscapeMarkdownV2("a=b");

        Assert.That(result, Is.EqualTo("a\\=b"));
    }

    [Test]
    public void EscapeMarkdownV2_Pipe_Escaped()
    {
        var result = TelegramTextUtilities.EscapeMarkdownV2("a|b");

        Assert.That(result, Is.EqualTo("a\\|b"));
    }

    [Test]
    public void EscapeMarkdownV2_CurlyBraces_Escaped()
    {
        var result = TelegramTextUtilities.EscapeMarkdownV2("{json}");

        Assert.That(result, Is.EqualTo("\\{json\\}"));
    }

    [Test]
    public void EscapeMarkdownV2_Period_Escaped()
    {
        var result = TelegramTextUtilities.EscapeMarkdownV2("end.");

        Assert.That(result, Is.EqualTo("end\\."));
    }

    [Test]
    public void EscapeMarkdownV2_ExclamationMark_Escaped()
    {
        var result = TelegramTextUtilities.EscapeMarkdownV2("Hello!");

        Assert.That(result, Is.EqualTo("Hello\\!"));
    }

    #endregion

    #region EscapeMarkdownV2 - Real-World Scenarios

    [Test]
    public void EscapeMarkdownV2_BanCelebrationCaption_Escaped()
    {
        var result = TelegramTextUtilities.EscapeMarkdownV2("GAME OVER for John! Insert coin to try again... just kidding.");

        Assert.That(result, Is.EqualTo("GAME OVER for John\\! Insert coin to try again\\.\\.\\. just kidding\\."));
    }

    [Test]
    public void EscapeMarkdownV2_HadoukenCaption_Escaped()
    {
        var result = TelegramTextUtilities.EscapeMarkdownV2("HADOUKEN! Steven blasted out of The Community!");

        Assert.That(result, Is.EqualTo("HADOUKEN\\! Steven blasted out of The Community\\!"));
    }

    [Test]
    public void EscapeMarkdownV2_NoScopeCaption_Escaped()
    {
        var result = TelegramTextUtilities.EscapeMarkdownV2("360 NO-SCOPE! Mike didn't see it coming!");

        Assert.That(result, Is.EqualTo("360 NO\\-SCOPE\\! Mike didn't see it coming\\!"));
    }

    [Test]
    public void EscapeMarkdownV2_MarkdownLink_FullyEscaped()
    {
        var result = TelegramTextUtilities.EscapeMarkdownV2("[Click here](https://example.com)");

        Assert.That(result, Is.EqualTo("\\[Click here\\]\\(https://example\\.com\\)"));
    }

    [Test]
    public void EscapeMarkdownV2_MultipleSpecialChars_AllEscaped()
    {
        var result = TelegramTextUtilities.EscapeMarkdownV2("*_~`>#+-=|{}.!");

        Assert.That(result, Is.EqualTo("\\*\\_\\~\\`\\>\\#\\+\\-\\=\\|\\{\\}\\.\\!"));
    }

    #endregion

    #region EscapeMarkdownV2 - Unicode and Special Content

    [Test]
    public void EscapeMarkdownV2_Emoji_PreservedUnescaped()
    {
        var result = TelegramTextUtilities.EscapeMarkdownV2("🎮 GAME OVER! 🔥");

        Assert.That(result, Is.EqualTo("🎮 GAME OVER\\! 🔥"));
    }

    [Test]
    public void EscapeMarkdownV2_CyrillicText_PreservedUnescaped()
    {
        var result = TelegramTextUtilities.EscapeMarkdownV2("Привет мир!");

        Assert.That(result, Is.EqualTo("Привет мир\\!"));
    }

    [Test]
    public void EscapeMarkdownV2_ChineseText_PreservedUnescaped()
    {
        var result = TelegramTextUtilities.EscapeMarkdownV2("你好世界!");

        Assert.That(result, Is.EqualTo("你好世界\\!"));
    }

    #endregion
}
