using NUnit.Framework;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Telegram.Repositories.Mappings;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Repositories.Mappings;

[TestFixture]
public class MessageMappingsTests
{
    [Test]
    public void ToModel_ExplicitFlag_MapsAuthorToExplicitVerdict()
    {
        var dto = new MessageRecordDto { MessageId = 1, UserId = 42, ChatId = -100 };

        var record = dto.ToModel(
            chatName: null, chatIconPath: null,
            userName: null, firstName: "Author", lastName: null, isBot: false, latestScanExplicit: true, latestScanPromotional: false, isBanned: true,
            userPhotoPath: null, replyToUser: null, replyToText: null);

        Assert.That(record.User.Verdict, Is.EqualTo(NameVerdict.Explicit));
        Assert.That(record.User.DisplayName, Is.EqualTo("Author"));
    }
}
