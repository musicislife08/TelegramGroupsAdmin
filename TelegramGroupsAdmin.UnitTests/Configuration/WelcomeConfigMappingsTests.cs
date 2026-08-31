using NUnit.Framework;
using TelegramGroupsAdmin.Configuration.Mappings;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Data.Models.Configs;

namespace TelegramGroupsAdmin.UnitTests.Configuration;

[TestFixture]
public class WelcomeConfigMappingsTests
{
    [Test]
    public void NewWelcomeConfig_HasTrustedBypassPopulated()
    {
        var config = new WelcomeConfig();

        Assert.That(config.TrustedBypass, Is.Not.Null);
        Assert.That(config.TrustedBypass.Enabled, Is.False);
        Assert.That(config.TrustedBypass.AnnouncementTtlSeconds, Is.EqualTo(30));
    }

    [Test]
    public void ToModel_PopulatesBothTemplateFields()
    {
        var data = new WelcomeConfigData
        {
            MainWelcomeMessage = "hi",
            TrustedBypass = new TrustedBypassConfigData
            {
                Enabled = true,
                AnnouncementMessageAdmin = "admin msg {username}",
                AnnouncementMessageTrusted = "trusted msg {username}",
                AnnouncementTtlSeconds = 45,
            }
        };
        var model = data.ToModel();
        Assert.Multiple(() =>
        {
            Assert.That(model.TrustedBypass.Enabled, Is.True);
            Assert.That(model.TrustedBypass.AnnouncementMessageAdmin, Is.EqualTo("admin msg {username}"));
            Assert.That(model.TrustedBypass.AnnouncementMessageTrusted, Is.EqualTo("trusted msg {username}"));
            Assert.That(model.TrustedBypass.AnnouncementTtlSeconds, Is.EqualTo(45));
        });
    }

    [Test]
    public void ToModel_NullTrustedBypass_ReturnsDefaults()
    {
        var data = new WelcomeConfigData { MainWelcomeMessage = "hi", TrustedBypass = null };
        var model = data.ToModel();
        Assert.That(model.TrustedBypass.Enabled, Is.False);
    }

    [Test]
    public void ToData_RoundtripsBothTemplateFields()
    {
        var model = new WelcomeConfig
        {
            MainWelcomeMessage = "hi",
            TrustedBypass =
            {
                Enabled = true,
                AnnouncementMessageAdmin = "a {username}",
                AnnouncementMessageTrusted = "t {username}",
                AnnouncementTtlSeconds = 60,
            }
        };
        var data = model.ToData();
        Assert.Multiple(() =>
        {
            Assert.That(data.TrustedBypass, Is.Not.Null);
            Assert.That(data.TrustedBypass!.Enabled, Is.True);
            Assert.That(data.TrustedBypass.AnnouncementMessageAdmin, Is.EqualTo("a {username}"));
            Assert.That(data.TrustedBypass.AnnouncementMessageTrusted, Is.EqualTo("t {username}"));
            Assert.That(data.TrustedBypass.AnnouncementTtlSeconds, Is.EqualTo(60));
        });
    }

    [Test]
    public void ProfileScanConfigData_ToModel_MapsScanOnFirstMessage()
    {
        var data = new ProfileScanConfigData { ScanOnFirstMessage = true };

        var model = data.ToModel();

        Assert.That(model.ScanOnFirstMessage, Is.True);
    }

    [Test]
    public void ProfileScanConfig_ToData_MapsScanOnFirstMessage()
    {
        var model = new ProfileScanConfig { ScanOnFirstMessage = true };

        var data = model.ToData();

        Assert.That(data.ScanOnFirstMessage, Is.True);
    }

    [Test]
    public void ProfileScanConfig_ScanOnFirstMessage_DefaultsToFalse()
    {
        // Default-off rollout: the absent scanOnFirstMessage key in existing
        // configs rows must deserialize to disabled.
        Assert.Multiple(() =>
        {
            Assert.That(new ProfileScanConfig().ScanOnFirstMessage, Is.False);
            Assert.That(new ProfileScanConfigData().ScanOnFirstMessage, Is.False);
        });
    }
}
