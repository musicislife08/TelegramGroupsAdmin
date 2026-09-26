using TelegramGroupsAdmin.Telegram.Services.DmCelebrations;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.DmCelebrations;

[TestFixture]
public class DmCelebrationDeepLinkTests
{
    [Test]
    public void Build_ProducesStartLinkWithChatIdPayload()
    {
        Assert.That(DmCelebrationDeepLink.Build("tga_bot", -100059667856554L),
            Is.EqualTo("https://t.me/tga_bot?start=dmcel_-100059667856554"));
    }

    [Test]
    public void TryParseChatId_RoundTripsNegativeChatId()
    {
        var ok = DmCelebrationDeepLink.TryParseChatId("dmcel_-100059667856554", out var chatId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ok, Is.True);
            Assert.That(chatId, Is.EqualTo(-100059667856554L));
        }
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("dmcel_")]
    [TestCase("dmcel_abc")]
    [TestCase("dmcel_-100_5")]
    [TestCase("welcome_-100059667856554_42")]
    [TestCase("DMCEL_-100059667856554")]
    public void TryParseChatId_RejectsForeignOrMalformedPayloads(string? payload)
    {
        Assert.That(DmCelebrationDeepLink.TryParseChatId(payload, out _), Is.False);
    }
}
