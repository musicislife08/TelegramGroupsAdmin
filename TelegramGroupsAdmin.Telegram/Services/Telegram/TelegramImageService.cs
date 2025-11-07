using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Telegram.Abstractions.Services;

namespace TelegramGroupsAdmin.Telegram.Services.Telegram;

public class TelegramImageService : ITelegramImageService
{
    private readonly TelegramBotClientFactory _botFactory;
    private readonly TelegramConfigLoader _configLoader;
    private readonly ILogger<TelegramImageService> _logger;

    public TelegramImageService(
        TelegramBotClientFactory botFactory,
        TelegramConfigLoader configLoader,
        ILogger<TelegramImageService> logger)
    {
        _botFactory = botFactory;
        _configLoader = configLoader;
        _logger = logger;
    }

    public async Task<Stream?> DownloadPhotoAsync(string fileId, CancellationToken ct = default)
    {
        try
        {
            var botToken = await _configLoader.LoadConfigAsync();
            var botClient = _botFactory.GetOrCreate(botToken);

            _logger.LogDebug("Downloading photo {FileId}", fileId);

            var file = await botClient.GetFile(fileId, ct);

            if (file.FilePath == null)
            {
                _logger.LogWarning("File path is null for {FileId}", fileId);
                return null;
            }

            var stream = new MemoryStream();
            await botClient.DownloadFile(file.FilePath, stream, ct);
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
