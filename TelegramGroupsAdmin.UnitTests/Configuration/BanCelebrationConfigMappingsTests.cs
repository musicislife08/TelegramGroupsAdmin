using System.Text.Json;
using NUnit.Framework;
using TelegramGroupsAdmin.Configuration.Mappings;
using TelegramGroupsAdmin.Data.Models.Configs;

namespace TelegramGroupsAdmin.UnitTests.Configuration;

[TestFixture]
public class BanCelebrationConfigMappingsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Test]
    public void RoundTrip_PreservesAllFourBooleans()
    {
        var model = new TelegramGroupsAdmin.Configuration.BanCelebrationConfig
        {
            Enabled = true,
            TriggerOnAutoBan = false,
            TriggerOnManualBan = true,
            SendToBannedUser = false
        };

        var json = JsonSerializer.Serialize(model.ToData(), JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<BanCelebrationConfigData>(json, JsonOptions)!.ToModel();

        Assert.Multiple(() =>
        {
            Assert.That(roundTripped.Enabled, Is.True);
            Assert.That(roundTripped.TriggerOnAutoBan, Is.False);
            Assert.That(roundTripped.TriggerOnManualBan, Is.True);
            Assert.That(roundTripped.SendToBannedUser, Is.False);
        });
    }
}
