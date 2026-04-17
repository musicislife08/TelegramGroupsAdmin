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
    public void ToModel_NullTrustedBypass_YieldsDefaults()
    {
        var data = new WelcomeConfigData
        {
            TrustedBypass = null,
        };

        var model = data.ToModel();

        Assert.That(model.TrustedBypass, Is.Not.Null);
        Assert.That(model.TrustedBypass.Enabled, Is.False);
        Assert.That(model.TrustedBypass.AnnouncementMessage, Is.EqualTo(TrustedBypassConfig.DefaultAnnouncementMessage));
    }

    [Test]
    public void ToDto_ThenToModel_RoundTripsTrustedBypass()
    {
        var original = new WelcomeConfig
        {
            TrustedBypass = new TrustedBypassConfig
            {
                Enabled = true,
                AnnouncementMessage = "hello {username}",
                AnnouncementTtlSeconds = 42,
            }
        };

        var dto = original.ToData();
        var roundTripped = dto.ToModel();

        Assert.That(roundTripped.TrustedBypass.Enabled, Is.True);
        Assert.That(roundTripped.TrustedBypass.AnnouncementMessage, Is.EqualTo("hello {username}"));
        Assert.That(roundTripped.TrustedBypass.AnnouncementTtlSeconds, Is.EqualTo(42));
    }
}
