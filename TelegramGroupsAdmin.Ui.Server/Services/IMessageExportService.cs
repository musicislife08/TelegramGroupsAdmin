using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Ui.Server.Services;

public interface IMessageExportService
{
    Task<byte[]> ExportToCsvAsync(
        IEnumerable<MessageRecord> messages,
        Dictionary<long, ContentCheckRecord?> contentChecks,
        CancellationToken cancellationToken = default);

    Task<byte[]> ExportToJsonAsync(
        IEnumerable<MessageRecord> messages,
        Dictionary<long, ContentCheckRecord?> contentChecks,
        CancellationToken cancellationToken = default);
}
