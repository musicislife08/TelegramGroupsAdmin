namespace TelegramGroupsAdmin.Telegram.Services.Telegram;

public interface ITelegramImageService
{
    Task<Stream?> DownloadPhotoAsync(string fileId, CancellationToken cancellationToken = default);
}
