using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Services.BotCommands;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.BotCommands;

[TestFixture]
public class PermissionResolverTests
{
    // webTier, isChatAdminOrCreator -> effective
    [TestCase(null, false, PermissionLevel.Member)]                       // unknown user
    [TestCase(null, true, PermissionLevel.Admin)]                        // native TG admin/creator
    [TestCase(PermissionLevel.Admin, false, PermissionLevel.Member)]     // web Admin in a chat they don't administer
    [TestCase(PermissionLevel.Admin, true, PermissionLevel.Admin)]       // web Admin who is also a chat admin
    [TestCase(PermissionLevel.GlobalAdmin, false, PermissionLevel.GlobalAdmin)] // global, any chat
    [TestCase(PermissionLevel.GlobalAdmin, true, PermissionLevel.GlobalAdmin)]
    [TestCase(PermissionLevel.Owner, false, PermissionLevel.Owner)]      // global, any chat
    [TestCase(PermissionLevel.Owner, true, PermissionLevel.Owner)]
    public void Resolve_MatchesCanonicalModel(PermissionLevel? webTier, bool isChatAdmin, PermissionLevel expected)
    {
        Assert.That(PermissionResolver.Resolve(webTier, isChatAdmin), Is.EqualTo(expected));
    }
}
