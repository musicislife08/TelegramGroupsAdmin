using NUnit.Framework;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Telegram.Repositories.Mappings;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Repositories.Mappings;

[TestFixture]
public class EnrichedMessageMappingsTests
{
    [Test]
    public void ToModel_ExplicitFlag_MapsAuthorToExplicitVerdict()
    {
        var view = new EnrichedMessageView
        {
            MessageId = 1, UserId = 42, ChatId = -100,
            FirstName = "Author", IsBot = false, LatestScanExplicit = true, IsBanned = true
        };

        var record = view.ToModel();

        Assert.That(record.User.Verdict, Is.EqualTo(NameVerdict.Explicit));
        Assert.That(record.User.DisplayName, Is.EqualTo("Author"));
    }

    [Test]
    public void ToModel_FlaggedAuthorNotBanned_MapsToClean()
    {
        var view = new EnrichedMessageView
        {
            MessageId = 1, UserId = 42, ChatId = -100,
            FirstName = "Author", IsBot = false, LatestScanExplicit = false, LatestScanPromotional = true, IsBanned = false
        };

        Assert.That(view.ToModel().User.Verdict, Is.EqualTo(NameVerdict.Clean));
    }
}
