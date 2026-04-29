using System.Text.Json;
using NUnit.Framework;
using TelegramGroupsAdmin.Configuration.Mappings;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Data.Models.Configs;

namespace TelegramGroupsAdmin.UnitTests.Configuration;

[TestFixture]
public class TelegramBotConfigMappingsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Test]
    public void RoundTrip_BotEnabled_True_Preserved()
    {
        var model = new TelegramBotConfig { BotEnabled = true };

        var json = JsonSerializer.Serialize(model.ToData(), JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<TelegramBotConfigData>(json, JsonOptions)!.ToModel();

        Assert.That(roundTripped.BotEnabled, Is.True);
    }

    [Test]
    public void RoundTrip_BotEnabled_False_Preserved()
    {
        var model = new TelegramBotConfig { BotEnabled = false };

        var json = JsonSerializer.Serialize(model.ToData(), JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<TelegramBotConfigData>(json, JsonOptions)!.ToModel();

        Assert.That(roundTripped.BotEnabled, Is.False);
    }
}
