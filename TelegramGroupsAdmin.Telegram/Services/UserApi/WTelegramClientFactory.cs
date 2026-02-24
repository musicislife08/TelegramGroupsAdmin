using Microsoft.Extensions.Logging;
using WTelegram;

namespace TelegramGroupsAdmin.Telegram.Services.UserApi;

/// <summary>
/// Production factory — creates real WTelegram.Client instances wrapped in WTelegramApiClient.
/// Singleton: redirects WTelegram's static logger to our ILogger on construction.
/// </summary>
public sealed class WTelegramClientFactory : IWTelegramClientFactory
{
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
        => new WTelegramApiClient(new Client(configCallback, sessionStore), _clientLogger);

    public IWTelegramApiClient Create(Func<string, string?> configCallback, byte[] startSession, Action<byte[]> saveSession)
        => new WTelegramApiClient(new Client(configCallback, startSession, saveSession), _clientLogger);
}
