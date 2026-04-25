using System.Text.Json;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using TelegramGroupsAdmin.Configuration.Mappings;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Data.Models.Configs;

namespace TelegramGroupsAdmin.UnitTests.Configuration;

[TestFixture]
public class LogConfigMappingsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Test]
    public void RoundTrip_PreservesAllFields()
    {
        var model = new LogConfig
        {
            DefaultLevel = LogLevel.Warning,
            Overrides = new Dictionary<string, LogLevel>
            {
                ["TelegramGroupsAdmin.Telegram"] = LogLevel.Debug,
                ["Microsoft.EntityFrameworkCore"] = LogLevel.Error
            },
            LastModified = new DateTimeOffset(2026, 4, 25, 12, 0, 0, TimeSpan.Zero)
        };

        var json = JsonSerializer.Serialize(model.ToData(), JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<LogConfigData>(json, JsonOptions)!.ToModel();

        Assert.Multiple(() =>
        {
            Assert.That(roundTripped.DefaultLevel, Is.EqualTo(LogLevel.Warning));
            Assert.That(roundTripped.Overrides, Has.Count.EqualTo(2));
            Assert.That(roundTripped.Overrides["TelegramGroupsAdmin.Telegram"], Is.EqualTo(LogLevel.Debug));
            Assert.That(roundTripped.Overrides["Microsoft.EntityFrameworkCore"], Is.EqualTo(LogLevel.Error));
            Assert.That(roundTripped.LastModified, Is.EqualTo(model.LastModified));
        });
    }

    [Test]
    public void ToData_DefaultLogLevel_MapsToInformationInt()
    {
        var model = new LogConfig { DefaultLevel = LogLevel.Information };
        var data = model.ToData();
        Assert.That(data.DefaultLevel, Is.EqualTo(2));
    }
}
