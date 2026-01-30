using Telegram.Bot;
using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;

/// <summary>
/// Low-level handler for Telegram media/file operations.
/// Thin wrapper around ITelegramBotClient - no business logic, just API calls.
/// This is the ONLY layer that should touch ITelegramBotClientFactory for media operations.
/// </summary>
public class BotMediaHandler(ITelegramBotClientFactory botClientFactory) : IBotMediaHandler
{
    private readonly ITelegramBotClientFactory _botClientFactory = botClientFactory;

    public async Task<UserProfilePhotos> GetUserProfilePhotosAsync(long userId, int offset = 0, int limit = 1, CancellationToken ct = default)
    {
        var client = await _botClientFactory.GetBotClientAsync();
        return await client.GetUserProfilePhotos(userId, offset: offset, limit: limit, cancellationToken: ct);
    }

    public async Task<TGFile> GetFileAsync(string fileId, CancellationToken ct = default)
    {
        var client = await _botClientFactory.GetBotClientAsync();
        return await client.GetFile(fileId, ct);
    }

    public async Task DownloadFileAsync(string filePath, Stream destination, CancellationToken ct = default)
    {
        var client = await _botClientFactory.GetBotClientAsync();
        await client.DownloadFile(filePath, destination, ct);
    }
}
