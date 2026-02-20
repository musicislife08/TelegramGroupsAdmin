namespace TelegramGroupsAdmin.Telegram.Services.UserApi;

/// <summary>
/// Factory for creating IWTelegramApiClient instances.
/// Handles WTelegram.Client construction, config callback wiring, and session store setup.
/// Same pattern as ITelegramBotClientFactory for the bot layer.
/// </summary>
public interface IWTelegramClientFactory
{
    /// <summary>Create a client with database-backed session storage (for reconnecting existing sessions).</summary>
    IWTelegramApiClient Create(Func<string, string?> configCallback, Stream sessionStore);

    /// <summary>Create a client with byte[]-based session (for new auth flows).</summary>
    IWTelegramApiClient Create(Func<string, string?> configCallback, byte[] startSession, Action<byte[]> saveSession);
}
