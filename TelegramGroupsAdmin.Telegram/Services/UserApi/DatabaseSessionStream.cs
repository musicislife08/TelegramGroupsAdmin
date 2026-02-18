using Microsoft.Extensions.Logging;

namespace TelegramGroupsAdmin.Telegram.Services.UserApi;

/// <summary>
/// Stream adapter that bridges WTelegram's session persistence API to our database.
/// WTelegram.Client requires a Stream in its constructor — it reads on startup and
/// writes+flushes after any session state change. This class wraps a MemoryStream and
/// calls back to ITelegramSessionRepository (via delegate) on flush.
///
/// The in-memory data is authoritative while the client is alive; DB persistence
/// is for restart recovery. Failed saves are logged but don't throw — the next
/// flush will retry with the latest state.
/// </summary>
public sealed class DatabaseSessionStream : MemoryStream
{
    private readonly Func<byte[], Task> _saveCallback;
    private readonly ILogger _logger;

    public DatabaseSessionStream(byte[] initialData, Func<byte[], Task> saveCallback, ILogger logger)
    {
        _saveCallback = saveCallback;
        _logger = logger;

        if (initialData.Length > 0)
        {
            Write(initialData, 0, initialData.Length);
            Position = 0;
        }
    }

    public override void Flush()
    {
        base.Flush();
        _ = PersistAsync();
    }

    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        await base.FlushAsync(cancellationToken);
        await PersistAsync();
    }

    private async Task PersistAsync()
    {
        try
        {
            await _saveCallback(ToArray());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist WTelegram session data to database — will retry on next flush");
        }
    }
}
