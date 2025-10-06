namespace TgSpam_PreFilterApi.Services.Telegram;

public interface ITelegramImageService
{
    Task<Stream?> DownloadPhotoAsync(string fileId, CancellationToken ct = default);
}
