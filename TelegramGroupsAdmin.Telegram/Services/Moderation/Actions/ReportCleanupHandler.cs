using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions;

/// <inheritdoc />
public sealed class ReportCleanupHandler(
    IReportsRepository reportsRepository,
    ILogger<ReportCleanupHandler> logger) : IReportCleanupHandler
{
    public async Task<int> CloseOpenReportsAsync(
        UserIdentity user,
        ChatIdentity? chat,
        Actor executor,
        string actionName,
        long? excludeReportId,
        CancellationToken cancellationToken = default)
    {
        var pending = await reportsRepository.GetPendingForUserAsync(user.Id, chat?.Id, cancellationToken);
        if (pending.Count == 0)
            return 0;

        // Plain chat id, not ToLogInfo() — this string is persisted in admin_notes,
        // so it must not carry a log-display format that can change underneath it.
        var scope = chat is null ? "globally" : $"in chat {chat.Id}";
        var note = $"Auto-resolved: user {actionName.ToLowerInvariant()}ed {scope}";

        var closed = 0;
        foreach (var report in pending)
        {
            if (excludeReportId.HasValue && report.Id == excludeReportId.Value)
                continue;

            // TryUpdateStatusAsync is atomic-on-pending: if an admin resolved this row
            // between the read and here, they win and we leave their decision alone.
            var updated = await reportsRepository.TryUpdateStatusAsync(
                report.Id,
                ReportStatus.Reviewed,
                executor.GetDisplayText(),
                $"Auto-{actionName}",
                note,
                cancellationToken);

            if (!updated)
                continue;

            closed++;
            logger.LogDebug("Auto-closed {ReportType} report #{ReportId} for {User}",
                report.Type, report.Id, user.ToLogDebug());
        }

        if (closed > 0)
        {
            logger.LogInformation(
                "Report cleanup: auto-closed {Count} open report(s) for {User} after {Action}",
                closed, user.ToLogInfo(), actionName);
        }

        return closed;
    }
}
