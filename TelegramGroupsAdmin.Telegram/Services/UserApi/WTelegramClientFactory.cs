using Microsoft.Extensions.Logging;
using WTelegram;

namespace TelegramGroupsAdmin.Telegram.Services.UserApi;

/// <summary>
/// Production factory — creates real WTelegram.Client instances wrapped in WTelegramApiClient.
/// Singleton: redirects WTelegram's static logger to our ILogger on construction.
/// </summary>
public sealed class WTelegramClientFactory : IWTelegramClientFactory
{
    /// <summary>
    /// WTelegram's default of 5 is a modulo counter that never resets on success, so every fifth
    /// dropped connection strands the client (#577). Each attempt waits 5 seconds first, so no limit cannot spin.
    /// </summary>
    internal const int UnlimitedAutoReconnects = int.MaxValue;

    private readonly ILogger<WTelegramApiClient> _clientLogger;

    public WTelegramClientFactory(ILogger<WTelegramClientFactory> logger, ILogger<WTelegramApiClient> clientLogger)
    {
        _clientLogger = clientLogger;

        // WTelegram.Helpers.Log is a static Action<int, string> where int maps directly
        // to Microsoft.Extensions.Logging.LogLevel enum values (0=Trace..5=Critical).
        // Redirect to our structured logger so WTelegram output flows through Serilog/Seq.
        WTelegram.Helpers.Log = (level, message) => logger.Log((LogLevel)level, "[WTelegram] {Message}", message);
    }

    public IWTelegramApiClient Create(Func<string, string?> configCallback, Stream sessionStore)
        => Wrap(new Client(configCallback, sessionStore));

    public IWTelegramApiClient Create(Func<string, string?> configCallback, byte[] startSession, Action<byte[]> saveSession)
        => Wrap(new Client(configCallback, startSession, saveSession));

    private WTelegramApiClient Wrap(Client client)
    {
        client.MaxAutoReconnects = UnlimitedAutoReconnects;
        return new WTelegramApiClient(client, _clientLogger);
    }
}
