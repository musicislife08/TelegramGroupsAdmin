using Microsoft.Extensions.Options;
using Telegram.Bot;
using TelegramGroupsAdmin.Configuration;

namespace TelegramGroupsAdmin.Services.Telegram;

public class TelegramImageService : ITelegramImageService
{
    private readonly TelegramBotClientFactory _botFactory;
    private readonly TelegramOptions _options;
    private readonly ILogger<TelegramImageService> _logger;

    public TelegramImageService(
        TelegramBotClientFactory botFactory,
        IOptions<TelegramOptions> options,
        ILogger<TelegramImageService> logger)
    {
        _botFactory = botFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Stream?> DownloadPhotoAsync(string fileId, CancellationToken ct = default)
    {
        try
        {
            var botClient = _botFactory.GetOrCreate(_options.BotToken);

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download photo {FileId}", fileId);
            return null;
        }
    }
}
