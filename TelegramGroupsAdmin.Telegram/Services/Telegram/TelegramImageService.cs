using Microsoft.Extensions.Logging;
using Telegram.Bot.Exceptions;

namespace TelegramGroupsAdmin.Telegram.Services.Telegram;

public class TelegramImageService : ITelegramImageService
{
    private readonly ITelegramBotClientFactory _botFactory;
    private readonly ILogger<TelegramImageService> _logger;

    public TelegramImageService(
        ITelegramBotClientFactory botFactory,
        ILogger<TelegramImageService> logger)
    {
        _botFactory = botFactory;
        _logger = logger;
    }

    public async Task<Stream?> DownloadPhotoAsync(string fileId, CancellationToken ct = default)
    {
        try
        {
            var operations = await _botFactory.GetOperationsAsync();

            _logger.LogDebug("Downloading photo {FileId}", fileId);

            var file = await operations.GetFileAsync(fileId, ct);

            if (file.FilePath == null)
            {
                _logger.LogWarning("File path is null for {FileId}", fileId);
                return null;
            }

            var stream = new MemoryStream();
            await operations.DownloadFileAsync(file.FilePath, stream, ct);
            stream.Position = 0;

            _logger.LogInformation(
                "Downloaded photo {FileId} ({Size} bytes)",
                fileId,
                stream.Length);

            return stream;
        }
        catch (ApiRequestException ex) when (ex.Message.Contains("file is too big"))
        {
            _logger.LogWarning(
                "Skipping photo download for {FileId}: File exceeds Telegram Bot API 20MB limit. " +
                "To download large photos, configure self-hosted Bot API server (Settings → Telegram Bot → API Server URL).",
                fileId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download photo {FileId}", fileId);
            return null;
        }
    }
}
