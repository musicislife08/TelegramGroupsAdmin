using System.Text.Json;
using NUnit.Framework;
using TelegramGroupsAdmin.Configuration.Mappings;
using TelegramGroupsAdmin.Data.Models.Configs;

namespace TelegramGroupsAdmin.UnitTests.Configuration;

[TestFixture]
public class ServiceMessageDeletionConfigMappingsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Test]
    public void RoundTrip_PreservesAllSixBooleans()
    {
        var model = new TelegramGroupsAdmin.Configuration.ServiceMessageDeletionConfig
        {
            DeleteJoinMessages = true,
            DeleteLeaveMessages = false,
            DeletePhotoChanges = true,
            DeleteTitleChanges = false,
            DeletePinNotifications = true,
            DeleteChatCreationMessages = false
        };

        var json = JsonSerializer.Serialize(model.ToData(), JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<ServiceMessageDeletionConfigData>(json, JsonOptions)!.ToModel();

        Assert.Multiple(() =>
        {
            Assert.That(roundTripped.DeleteJoinMessages, Is.True);
            Assert.That(roundTripped.DeleteLeaveMessages, Is.False);
            Assert.That(roundTripped.DeletePhotoChanges, Is.True);
            Assert.That(roundTripped.DeleteTitleChanges, Is.False);
            Assert.That(roundTripped.DeletePinNotifications, Is.True);
            Assert.That(roundTripped.DeleteChatCreationMessages, Is.False);
        });
    }

    [Test]
    public void RoundTrip_DefaultValues_AllTrue()
    {
        var json = JsonSerializer.Serialize(new ServiceMessageDeletionConfigData(), JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<ServiceMessageDeletionConfigData>(json, JsonOptions)!.ToModel();
        Assert.Multiple(() =>
        {
            Assert.That(roundTripped.DeleteJoinMessages, Is.True);
            Assert.That(roundTripped.DeleteLeaveMessages, Is.True);
            Assert.That(roundTripped.DeletePhotoChanges, Is.True);
            Assert.That(roundTripped.DeleteTitleChanges, Is.True);
            Assert.That(roundTripped.DeletePinNotifications, Is.True);
            Assert.That(roundTripped.DeleteChatCreationMessages, Is.True);
        });
    }
}
