using System.Text.Json;
using NUnit.Framework;
using TelegramGroupsAdmin.Configuration.Mappings;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Data.Models.Configs;

namespace TelegramGroupsAdmin.UnitTests.Configuration;

[TestFixture]
public class InviteCommandConfigMappingsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Test]
    public void RoundTrip_PreservesAllFields()
    {
        var model = new InviteCommandConfig
        {
            Enabled = true,
            DeleteCommandMessage = false,
            DeleteResponseAfterSeconds = 60
        };

        var json = JsonSerializer.Serialize(model.ToData(), JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<InviteCommandConfigData>(json, JsonOptions)!.ToModel();

        Assert.Multiple(() =>
        {
            Assert.That(roundTripped.Enabled, Is.True);
            Assert.That(roundTripped.DeleteCommandMessage, Is.False);
            Assert.That(roundTripped.DeleteResponseAfterSeconds, Is.EqualTo(60));
        });
    }
}
