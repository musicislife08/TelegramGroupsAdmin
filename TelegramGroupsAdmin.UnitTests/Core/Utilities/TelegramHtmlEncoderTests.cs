using NUnit.Framework;
using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.UnitTests.Core.Utilities;

[TestFixture]
public class TelegramHtmlEncoderTests
{
    [Test]
    public void Encode_Null_ReturnsEmpty()
        => Assert.That(TelegramHtmlEncoder.Encode(null), Is.EqualTo(string.Empty));

    [Test]
    public void Encode_Empty_ReturnsEmpty()
        => Assert.That(TelegramHtmlEncoder.Encode(""), Is.EqualTo(string.Empty));

    [Test]
    public void Encode_PlainText_ReturnsUnchanged()
        => Assert.That(TelegramHtmlEncoder.Encode("Hello"), Is.EqualTo("Hello"));

    [Test]
    public void Encode_HtmlTags_AreEscaped()
        => Assert.That(TelegramHtmlEncoder.Encode("<b>x</b>"), Is.EqualTo("&lt;b&gt;x&lt;/b&gt;"));

    [Test]
    public void Encode_AmpersandAndQuotes_AreEscaped()
        => Assert.That(TelegramHtmlEncoder.Encode("a & \"b\""), Is.EqualTo("a &amp; &quot;b&quot;"));
}
