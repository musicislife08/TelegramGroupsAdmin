using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.UnitTests.Core.Models;

[TestFixture]
public class PermissionLevelTests
{
    [Test]
    public void Member_IsMinusOne_AndBelowAdmin()
    {
        Assert.That((int)PermissionLevel.Member, Is.EqualTo(-1));
        Assert.That(PermissionLevel.Member, Is.LessThan(PermissionLevel.Admin));
    }

    [TestCase(PermissionLevel.Member, "Member")]
    [TestCase(PermissionLevel.Admin, "Admin")]
    [TestCase(PermissionLevel.GlobalAdmin, "GlobalAdmin")]
    [TestCase(PermissionLevel.Owner, "Owner")]
    public void GetDisplayName_ReturnsDisplayAttributeName(PermissionLevel level, string expected)
    {
        Assert.That(level.GetDisplayName(), Is.EqualTo(expected));
    }

    [Test]
    public void StoredTiers_KeepExistingIntValues()
    {
        // Web claims/policies depend on these — must not change.
        Assert.That((int)PermissionLevel.Admin, Is.EqualTo(0));
        Assert.That((int)PermissionLevel.GlobalAdmin, Is.EqualTo(1));
        Assert.That((int)PermissionLevel.Owner, Is.EqualTo(2));
    }
}
