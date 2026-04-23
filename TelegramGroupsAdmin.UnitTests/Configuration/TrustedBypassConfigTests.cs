using NUnit.Framework;
using TelegramGroupsAdmin.Configuration.Models.Welcome;

namespace TelegramGroupsAdmin.UnitTests.Configuration;

[TestFixture]
public class TrustedBypassConfigTests
{
    [Test]
    public void Defaults_Enabled_IsFalse()
        => Assert.That(new TrustedBypassConfig().Enabled, Is.False);

    [Test]
    public void Defaults_AdminTemplate_ReferencesUsernameVariable()
        => Assert.That(new TrustedBypassConfig().AnnouncementMessageAdmin,
            Does.Contain(TrustedBypassConfig.UsernameVariable));

    [Test]
    public void Defaults_TrustedTemplate_ReferencesUsernameVariable()
        => Assert.That(new TrustedBypassConfig().AnnouncementMessageTrusted,
            Does.Contain(TrustedBypassConfig.UsernameVariable));

    [Test]
    public void Constants_Match_Spec()
    {
        Assert.That(TrustedBypassConfig.MinAnnouncementTtlSeconds, Is.EqualTo(0));
        Assert.That(TrustedBypassConfig.MaxAnnouncementTemplateLength, Is.EqualTo(3500));
        Assert.That(TrustedBypassConfig.UsernameVariable, Is.EqualTo("{username}"));
        Assert.That(TrustedBypassConfig.ChatNameVariable, Is.EqualTo("{chat_name}"));
    }

    [Test]
    public void Defaults_AnnouncementTtlSeconds_Is30()
        => Assert.That(new TrustedBypassConfig().AnnouncementTtlSeconds, Is.EqualTo(30));
}
