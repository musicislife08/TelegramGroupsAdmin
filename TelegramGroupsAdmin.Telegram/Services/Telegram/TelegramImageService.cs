using Microsoft.Extensions.Logging;
using Telegram.Bot.Exceptions;
using TelegramGroupsAdmin.Telegram.Services.Bot;

namespace TelegramGroupsAdmin.Telegram.Services.Telegram;

public class TelegramImageService : ITelegramImageService
{
    private readonly IBotMediaService _mediaService;
    private readonly ILogger<TelegramImageService> _logger;

    public TelegramImageService(
        IBotMediaService mediaService,
        ILogger<TelegramImageService> logger)
    {
        _mediaService = mediaService;
        _logger = logger;
    }

    public async Task<Stream?> DownloadPhotoAsync(string fileId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Downloading photo {FileId}", fileId);

            var file = await _mediaService.GetFileAsync(fileId, cancellationToken);

            if (file.FilePath == null)
            {
                _logger.LogWarning("File path is null for {FileId}", fileId);
                return null;
            }

            var stream = new MemoryStream();
            await _mediaService.DownloadFileAsync(file.FilePath, stream, cancellationToken);
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
