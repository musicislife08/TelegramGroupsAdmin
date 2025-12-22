using TelegramGroupsAdmin.Core;

namespace TelegramGroupsAdmin.UnitTests.Utilities;

/// <summary>
/// Unit tests for TelegramConstants.
/// Tests IsSystemUser() method for all known Telegram system accounts.
/// </summary>
[TestFixture]
public class TelegramConstantsTests
{
    #region IsSystemUser Tests

    [Test]
    [TestCase(777000, Description = "Service account for channel posts")]
    [TestCase(1087968824, Description = "Anonymous admin bot")]
    [TestCase(136817688, Description = "Channel bot")]
    [TestCase(1271266957, Description = "Replies bot")]
    [TestCase(5434988373, Description = "Antispam bot")]
    public void IsSystemUser_KnownSystemUserId_ReturnsTrue(long userId)
    {
        Assert.That(TelegramConstants.IsSystemUser(userId), Is.True);
    }

    [Test]
    [TestCase(12345, Description = "Regular user")]
    [TestCase(0, Description = "Zero user ID")]
    [TestCase(-1, Description = "Negative user ID")]
    [TestCase(999999999, Description = "Large regular user ID")]
    [TestCase(777001, Description = "Close to service account but not")]
    public void IsSystemUser_RegularUserId_ReturnsFalse(long userId)
    {
        Assert.That(TelegramConstants.IsSystemUser(userId), Is.False);
    }

    #endregion

    #region GetSystemUserName Tests

    [Test]
    public void GetSystemUserName_ServiceAccountUserId_ReturnsTelegramServiceAccount()
    {
        var result = TelegramConstants.GetSystemUserName(TelegramConstants.ServiceAccountUserId);
        Assert.That(result, Is.EqualTo("Telegram Service Account"));
    }

    [Test]
    public void GetSystemUserName_GroupAnonymousBotUserId_ReturnsAnonymousAdmin()
    {
        var result = TelegramConstants.GetSystemUserName(TelegramConstants.GroupAnonymousBotUserId);
        Assert.That(result, Is.EqualTo("Anonymous Admin"));
    }

    [Test]
    public void GetSystemUserName_ChannelBotUserId_ReturnsChannelBot()
    {
        var result = TelegramConstants.GetSystemUserName(TelegramConstants.ChannelBotUserId);
        Assert.That(result, Is.EqualTo("Channel Bot"));
    }

    [Test]
    public void GetSystemUserName_RepliesBotUserId_ReturnsRepliesBot()
    {
        var result = TelegramConstants.GetSystemUserName(TelegramConstants.RepliesBotUserId);
        Assert.That(result, Is.EqualTo("Replies Bot"));
    }

    [Test]
    public void GetSystemUserName_AntispamBotUserId_ReturnsTelegramAntispam()
    {
        var result = TelegramConstants.GetSystemUserName(TelegramConstants.AntispamBotUserId);
        Assert.That(result, Is.EqualTo("Telegram Antispam"));
    }

    [Test]
    public void GetSystemUserName_RegularUserId_ReturnsNull()
    {
        var result = TelegramConstants.GetSystemUserName(12345);
        Assert.That(result, Is.Null);
    }

    #endregion

    #region Constant Values Verification

    [Test]
    public void ServiceAccountUserId_IsCorrectValue()
    {
        Assert.That(TelegramConstants.ServiceAccountUserId, Is.EqualTo(777000));
    }

    [Test]
    public void GroupAnonymousBotUserId_IsCorrectValue()
    {
        Assert.That(TelegramConstants.GroupAnonymousBotUserId, Is.EqualTo(1087968824));
    }

    [Test]
    public void ChannelBotUserId_IsCorrectValue()
    {
        Assert.That(TelegramConstants.ChannelBotUserId, Is.EqualTo(136817688));
    }

    [Test]
    public void RepliesBotUserId_IsCorrectValue()
    {
        Assert.That(TelegramConstants.RepliesBotUserId, Is.EqualTo(1271266957));
    }

    [Test]
    public void AntispamBotUserId_IsCorrectValue()
    {
        Assert.That(TelegramConstants.AntispamBotUserId, Is.EqualTo(5434988373));
    }

    #endregion
}
