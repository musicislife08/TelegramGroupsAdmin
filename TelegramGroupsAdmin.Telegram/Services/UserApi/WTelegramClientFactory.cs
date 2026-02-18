using WTelegram;

namespace TelegramGroupsAdmin.Telegram.Services.UserApi;

/// <summary>
/// Production factory — creates real WTelegram.Client instances wrapped in WTelegramApiClient.
/// </summary>
public sealed class WTelegramClientFactory : IWTelegramClientFactory
{
    public IWTelegramApiClient Create(Func<string, string?> configCallback, Stream sessionStore)
        => new WTelegramApiClient(new Client(configCallback, sessionStore));

    public IWTelegramApiClient Create(Func<string, string?> configCallback, byte[] startSession, Action<byte[]> saveSession)
        => new WTelegramApiClient(new Client(configCallback, startSession, saveSession));
}
