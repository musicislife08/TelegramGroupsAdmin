using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.UnitTests.Utilities;

/// <summary>
/// AuditReason.WithChatTag composes an audit row's description at write time: the chat, formatted
/// by the log formatter, as a leading tag. The tag never produces a dangling fragment when the
/// caller supplied no reason (Delete rows have none).
/// </summary>
[TestFixture]
public class AuditReasonTests
{
    private static readonly ChatIdentity NamedChat = new(-100026957614982L, "Main Community");
    private static readonly ChatIdentity UnnamedChat = ChatIdentity.FromId(-100055500000001L);

    [Test]
    public void WithChatTag_NullChat_ReturnsReasonUnchanged()
    {
        Assert.That(AuditReason.WithChatTag(null, "Manually marked as spam"), Is.EqualTo("Manually marked as spam"));
    }

    [Test]
    public void WithChatTag_NullChatAndNullReason_ReturnsNull()
    {
        Assert.That(AuditReason.WithChatTag(null, null), Is.Null);
    }

    [Test]
    public void WithChatTag_NamedChatWithReason_PrefixesTag()
    {
        Assert.That(AuditReason.WithChatTag(NamedChat, "Welcome timeout"), Is.EqualTo("[Main Community] Welcome timeout"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void WithChatTag_NamedChatWithoutReason_ReturnsTagOnly(string? reason)
    {
        Assert.That(AuditReason.WithChatTag(NamedChat, reason), Is.EqualTo("[Main Community]"));
    }

    [Test]
    public void WithChatTag_UnnamedChat_UsesLogFormatterFallback()
    {
        Assert.That(AuditReason.WithChatTag(UnnamedChat, "Welcome timeout"), Is.EqualTo("[Chat -100055500000001] Welcome timeout"));
    }
}
