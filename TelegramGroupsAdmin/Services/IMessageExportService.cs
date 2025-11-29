using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Services;

public interface IMessageExportService
{
    Task<byte[]> ExportToCsvAsync(IEnumerable<MessageRecord> messages, Dictionary<long, ContentCheckRecord?> contentChecks);
    Task<byte[]> ExportToJsonAsync(IEnumerable<MessageRecord> messages, Dictionary<long, ContentCheckRecord?> contentChecks);
}
