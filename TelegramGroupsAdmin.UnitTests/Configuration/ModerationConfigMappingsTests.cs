using NUnit.Framework;
using TelegramGroupsAdmin.Configuration.Mappings;
using TelegramGroupsAdmin.Data.Models.Configs;

namespace TelegramGroupsAdmin.UnitTests.Configuration;

[TestFixture]
public class ModerationConfigMappingsTests
{
    [Test]
    public void WithWarningSystem_PreservesInviteCommand()
    {
        var original = new ModerationConfigData
        {
            WarningSystem = new WarningSystemConfigData { AutoBanThreshold = 3 },
            InviteCommand = new InviteCommandConfigData { Enabled = true, DeleteResponseAfterSeconds = 60 }
        };

        var updated = original.WithWarningSystem(new WarningSystemConfigData { AutoBanThreshold = 5 });

        Assert.Multiple(() =>
        {
            Assert.That(updated.WarningSystem!.AutoBanThreshold, Is.EqualTo(5));
            Assert.That(updated.InviteCommand!.Enabled, Is.True);
            Assert.That(updated.InviteCommand!.DeleteResponseAfterSeconds, Is.EqualTo(60));
        });
    }

    [Test]
    public void WithInviteCommand_PreservesWarningSystem()
    {
        var original = new ModerationConfigData
        {
            WarningSystem = new WarningSystemConfigData { AutoBanThreshold = 3 },
            InviteCommand = new InviteCommandConfigData { Enabled = false }
        };

        var updated = original.WithInviteCommand(new InviteCommandConfigData { Enabled = true });

        Assert.Multiple(() =>
        {
            Assert.That(updated.InviteCommand!.Enabled, Is.True);
            Assert.That(updated.WarningSystem!.AutoBanThreshold, Is.EqualTo(3));
        });
    }
}
