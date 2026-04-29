using System.Text.Json;
using NUnit.Framework;
using TelegramGroupsAdmin.Configuration.Mappings;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Data.Models.Configs;

namespace TelegramGroupsAdmin.UnitTests.Configuration;

[TestFixture]
public class BotProtectionConfigMappingsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Test]
    public void RoundTrip_PreservesAllFields()
    {
        var model = new BotProtectionConfig
        {
            Enabled = true,
            AutoBanBots = true,
            AllowAdminInvitedBots = false,
            WhitelistedBots = ["@RoseBot", "@GroupButlerBot"],
            LogBotEvents = true
        };

        var json = JsonSerializer.Serialize(model.ToData(), JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<BotProtectionConfigData>(json, JsonOptions)!.ToModel();

        Assert.Multiple(() =>
        {
            Assert.That(roundTripped.Enabled, Is.True);
            Assert.That(roundTripped.AutoBanBots, Is.True);
            Assert.That(roundTripped.AllowAdminInvitedBots, Is.False);
            Assert.That(roundTripped.WhitelistedBots, Is.EqualTo(new[] { "@RoseBot", "@GroupButlerBot" }));
            Assert.That(roundTripped.LogBotEvents, Is.True);
        });
    }

    [Test]
    public void RoundTrip_EmptyWhitelistedBots_RemainsEmpty()
    {
        var model = new BotProtectionConfig
        {
            Enabled = false,
            AutoBanBots = false,
            AllowAdminInvitedBots = true,
            WhitelistedBots = [],
            LogBotEvents = false
        };

        var json = JsonSerializer.Serialize(model.ToData(), JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<BotProtectionConfigData>(json, JsonOptions)!.ToModel();

        Assert.That(roundTripped.WhitelistedBots, Is.Empty);
    }
}
