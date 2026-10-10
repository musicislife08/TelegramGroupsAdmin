using NUnit.Framework;
using TelegramGroupsAdmin.Core;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories.Mappings;

namespace TelegramGroupsAdmin.UnitTests.Core.Repositories.Mappings;

[TestFixture]
public class UserIdentityMappingTests
{
    [TestCase(false, true, false, true, NameVerdict.Explicit)]
    [TestCase(false, true, true, true, NameVerdict.Explicit)]      // explicit beats promotional
    [TestCase(false, false, true, true, NameVerdict.Promotional)]
    [TestCase(false, false, false, true, NameVerdict.Clean)]
    [TestCase(false, true, false, false, NameVerdict.Clean)]       // flagged but not banned: shown normally
    [TestCase(false, false, true, false, NameVerdict.Clean)]
    [TestCase(false, false, false, false, NameVerdict.Clean)]
    [TestCase(false, null, null, true, NameVerdict.Unscanned)]
    [TestCase(false, null, null, false, NameVerdict.Unscanned)]
    [TestCase(true, true, true, true, NameVerdict.Unscanned)]      // bots are never judged
    public void ToIdentity_AppliesVerdictRule(
        bool isBot, bool? latestScanExplicit, bool? latestScanPromotional, bool isBanned, NameVerdict expected)
    {
        var identity = UserIdentityMapping.ToIdentity(42, "A", null, null, isBot, latestScanExplicit, latestScanPromotional, isBanned);

        Assert.That(identity.Verdict, Is.EqualTo(expected));
        Assert.That(identity.DisplayName, Is.EqualTo("A"));
    }

    [Test]
    public void ToIdentity_SystemAccount_IsUnscanned()
    {
        var identity = UserIdentityMapping.ToIdentity(
            TelegramConstants.ServiceAccountUserId, "Telegram", null, null, false, true, true, true);

        Assert.That(identity.Verdict, Is.EqualTo(NameVerdict.Unscanned));
    }
}
