using Telegram.Bot.Exceptions;
using TelegramGroupsAdmin.Telegram.Helpers;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Helpers;

[TestFixture]
public class TelegramFileIdErrorsTests
{
    [TestCase("Bad Request: wrong file identifier/HTTP URL specified")]
    [TestCase("Bad Request: invalid file_id")]
    [TestCase("Bad Request: INVALID FILE")]
    public void IsInvalidFileId_FileIdErrors_ReturnsTrue(string message)
    {
        Assert.That(TelegramFileIdErrors.IsInvalidFileId(new ApiRequestException(message, 400)), Is.True);
    }

    [TestCase("Forbidden: bot was blocked by the user")]
    [TestCase("Bad Request: chat not found")]
    public void IsInvalidFileId_OtherErrors_ReturnsFalse(string message)
    {
        Assert.That(TelegramFileIdErrors.IsInvalidFileId(new ApiRequestException(message, 400)), Is.False);
    }
}
