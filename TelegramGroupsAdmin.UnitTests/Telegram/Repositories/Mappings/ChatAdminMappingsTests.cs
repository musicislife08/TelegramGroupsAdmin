using NUnit.Framework;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Telegram.Repositories.Mappings;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Repositories.Mappings;

[TestFixture]
public class ChatAdminMappingsTests
{
    [Test]
    public void ToModel_ExplicitFlag_MapsAdminToExplicitVerdict()
    {
        var dto = new ChatAdminRecordDto { Id = 1, ChatId = -100, TelegramId = 42 };
        var identity = new UserIdentityView { TelegramUserId = 42, FirstName = "Admin", LatestScanExplicit = true };

        var admin = dto.ToModel(identity);

        Assert.That(admin.User.Verdict, Is.EqualTo(NameVerdict.Explicit));
        Assert.That(admin.User.DisplayName, Is.EqualTo("Admin"));
    }

    [Test]
    public void ToModel_NoIdentityRow_FallsBackToIdOnly()
    {
        var dto = new ChatAdminRecordDto { Id = 1, ChatId = -100, TelegramId = 42 };

        var admin = dto.ToModel(null);

        Assert.That(admin.User, Is.EqualTo(UserIdentity.FromId(42)));
    }
}
