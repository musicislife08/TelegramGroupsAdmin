using NUnit.Framework;
using TelegramGroupsAdmin.Core;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories.Mappings;

namespace TelegramGroupsAdmin.UnitTests.Core.Repositories.Mappings;

[TestFixture]
public class UserIdentityMappingTests
{
    [TestCase(false, true, NameVerdict.Explicit)]
    [TestCase(false, false, NameVerdict.Clean)]
    [TestCase(false, null, NameVerdict.Unscanned)]
    [TestCase(true, true, NameVerdict.Unscanned)] // bots are never judged
    public void ToIdentity_AppliesVerdictRule(bool isBot, bool? latestScanExplicit, NameVerdict expected)
    {
        var identity = UserIdentityMapping.ToIdentity(42, "A", null, null, isBot, latestScanExplicit);

        Assert.That(identity.Verdict, Is.EqualTo(expected));
        Assert.That(identity.DisplayName, Is.EqualTo("A"));
    }

    [Test]
    public void ToIdentity_SystemAccount_IsUnscanned()
    {
        var identity = UserIdentityMapping.ToIdentity(TelegramConstants.ServiceAccountUserId, "Telegram", null, null, false, true);

        Assert.That(identity.Verdict, Is.EqualTo(NameVerdict.Unscanned));
    }
}
