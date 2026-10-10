using Microsoft.Extensions.Logging;
using NSubstitute;
using TelegramGroupsAdmin.Telegram.Services.UserApi;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.UserApi;

/// <summary>
/// Builds real WTelegram.Client instances. Constructing one only loads the session from the given
/// stream; nothing connects until login, so these tests never touch the network.
/// </summary>
[TestFixture]
public class WTelegramClientFactoryTests
{
    // WTelegram only checks the hash is hex at construction; a fixed fake keeps secret scanners quiet
    private static readonly string FakeApiHash = new('a', 32);

    private static string? Config(string what) => what switch
    {
        "api_id" => "12345",
        "api_hash" => FakeApiHash,
        _ => null
    };

    private static WTelegramClientFactory MakeFactory() =>
        new(Substitute.For<ILogger<WTelegramClientFactory>>(), Substitute.For<ILogger<WTelegramApiClient>>());

    [Test]
    public async Task Create_WithSessionStream_KeepsReconnectingAfterRepeatedResets()
    {
        await using var client = (WTelegramApiClient)MakeFactory().Create(Config, new MemoryStream());

        Assert.That(client.MaxAutoReconnects, Is.EqualTo(WTelegramClientFactory.UnlimitedAutoReconnects));
    }

    [Test]
    public async Task Create_WithSessionBytes_KeepsReconnectingAfterRepeatedResets()
    {
        await using var client = (WTelegramApiClient)MakeFactory().Create(Config, null!, _ => { });

        Assert.That(client.MaxAutoReconnects, Is.EqualTo(WTelegramClientFactory.UnlimitedAutoReconnects));
    }
}
