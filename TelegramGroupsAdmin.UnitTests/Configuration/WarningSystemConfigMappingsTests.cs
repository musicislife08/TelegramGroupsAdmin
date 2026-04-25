using System.Text.Json;
using NUnit.Framework;
using TelegramGroupsAdmin.Configuration.Mappings;
using TelegramGroupsAdmin.Data.Models.Configs;

namespace TelegramGroupsAdmin.UnitTests.Configuration;

[TestFixture]
public class WarningSystemConfigMappingsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Test]
    public void RoundTrip_PreservesAllFields()
    {
        var model = new TelegramGroupsAdmin.Configuration.WarningSystemConfig
        {
            AutoBanEnabled = true,
            AutoBanThreshold = 5,
            AutoBanReason = "Automatic ban after {count} warnings"
        };

        var json = JsonSerializer.Serialize(model.ToData(), JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<WarningSystemConfigData>(json, JsonOptions)!.ToModel();

        Assert.Multiple(() =>
        {
            Assert.That(roundTripped.AutoBanEnabled, Is.True);
            Assert.That(roundTripped.AutoBanThreshold, Is.EqualTo(5));
            Assert.That(roundTripped.AutoBanReason, Is.EqualTo("Automatic ban after {count} warnings"));
        });
    }
}
