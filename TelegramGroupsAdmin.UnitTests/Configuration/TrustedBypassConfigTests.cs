using System.Text.Json;
using NUnit.Framework;
using TelegramGroupsAdmin.Configuration.Models.Welcome;

namespace TelegramGroupsAdmin.UnitTests.Configuration;

[TestFixture]
public class TrustedBypassConfigTests
{
    [Test]
    public void DefaultConstruction_ProducesExpectedDefaults()
    {
        var config = new TrustedBypassConfig();

        Assert.That(config.Enabled, Is.False);
        Assert.That(config.AnnouncementMessage, Is.EqualTo(TrustedBypassConfig.DefaultAnnouncementMessage));
        Assert.That(config.AnnouncementTtlSeconds, Is.EqualTo(TrustedBypassConfig.DefaultAnnouncementTtlSeconds));
    }

    [Test]
    public void DefaultAnnouncementMessage_ContainsUsernameVariable()
    {
        Assert.That(TrustedBypassConfig.DefaultAnnouncementMessage,
            Does.Contain(TrustedBypassConfig.UsernameVariable));
    }

    [Test]
    public void JsonRoundTrip_PreservesDefaults()
    {
        var original = new TrustedBypassConfig();
        var json = JsonSerializer.Serialize(original);
        var roundTripped = JsonSerializer.Deserialize<TrustedBypassConfig>(json)!;

        Assert.That(roundTripped.Enabled, Is.EqualTo(original.Enabled));
        Assert.That(roundTripped.AnnouncementMessage, Is.EqualTo(original.AnnouncementMessage));
        Assert.That(roundTripped.AnnouncementTtlSeconds, Is.EqualTo(original.AnnouncementTtlSeconds));
    }

    [Test]
    public void JsonRoundTrip_PreservesCustomValues()
    {
        var original = new TrustedBypassConfig
        {
            Enabled = true,
            AnnouncementMessage = "custom {username}",
            AnnouncementTtlSeconds = 45,
        };
        var json = JsonSerializer.Serialize(original);
        var roundTripped = JsonSerializer.Deserialize<TrustedBypassConfig>(json)!;

        Assert.That(roundTripped.Enabled, Is.True);
        Assert.That(roundTripped.AnnouncementMessage, Is.EqualTo("custom {username}"));
        Assert.That(roundTripped.AnnouncementTtlSeconds, Is.EqualTo(45));
    }
}
