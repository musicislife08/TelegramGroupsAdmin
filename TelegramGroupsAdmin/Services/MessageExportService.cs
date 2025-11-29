using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using CsvHelper;
using Microsoft.AspNetCore.Components.Authorization;
using TelegramGroupsAdmin.Auth;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Services;

public class MessageExportService(
    AuthenticationStateProvider authStateProvider,
    IAuditService auditService) : IMessageExportService
{
    private async Task<(int PermissionLevel, string? UserId)> GetUserInfoAsync()
    {
        var authState = await authStateProvider.GetAuthenticationStateAsync();
        var user = authState.User;

        if (user.Identity?.IsAuthenticated != true)
            return (-1, null);

        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var permissionClaim = user.FindFirst(CustomClaimTypes.PermissionLevel);
        var level = permissionClaim != null && int.TryParse(permissionClaim.Value, out var l) ? l : 0;

        return (level, userId);
    }

    private async Task<string?> ValidateExportPermissionAsync()
    {
        var (permissionLevel, userId) = await GetUserInfoAsync();
        if (permissionLevel < 1) // Require Admin+ (level >= 1)
        {
            throw new UnauthorizedAccessException("Export feature requires Admin or Owner permission level.");
        }
        return userId;
    }
    public async Task<byte[]> ExportToCsvAsync(
        IEnumerable<MessageRecord> messages,
        Dictionary<long, ContentCheckRecord?> contentChecks,
        CancellationToken ct = default)
    {
        // Validate permission (Admin+ required)
        var userId = await ValidateExportPermissionAsync();

        var messageList = messages.ToList();
        var messageCount = messageList.Count;

        using var memoryStream = new MemoryStream();
        using var writer = new StreamWriter(memoryStream, Encoding.UTF8);
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

        // Write headers
        csv.WriteField("Message ID");
        csv.WriteField("Timestamp");
        csv.WriteField("Date/Time");
        csv.WriteField("User ID");
        csv.WriteField("User Name");
        csv.WriteField("Chat ID");
        csv.WriteField("Chat Name");
        csv.WriteField("Message Text");
        csv.WriteField("URLs");
        csv.WriteField("Has Image");
        csv.WriteField("Image File ID");
        csv.WriteField("Edit Date");
        csv.WriteField("Is Edited");
        csv.WriteField("Content Hash");
        csv.WriteField("Spam Status");
        csv.WriteField("Spam Confidence");
        csv.WriteField("Spam Reason");
        csv.WriteField("Check Type");
        await csv.NextRecordAsync();

        // Write data
        foreach (var message in messageList)
        {
            var contentCheck = contentChecks.GetValueOrDefault(message.MessageId);
            var timestamp = message.Timestamp;

            csv.WriteField(message.MessageId);
            csv.WriteField(message.Timestamp);
            csv.WriteField(timestamp.ToString("yyyy-MM-dd HH:mm:ss"));
            csv.WriteField(message.UserId);
            csv.WriteField(message.UserName ?? string.Empty);
            csv.WriteField(message.ChatId);
            csv.WriteField(message.ChatName ?? string.Empty);
            csv.WriteField(message.MessageText ?? string.Empty);
            csv.WriteField(message.Urls ?? string.Empty);
            csv.WriteField(!string.IsNullOrEmpty(message.PhotoFileId));
            csv.WriteField(message.PhotoFileId ?? string.Empty);
            csv.WriteField(message.EditDate?.ToString() ?? string.Empty);
            csv.WriteField(message.EditDate.HasValue);
            csv.WriteField(message.ContentHash ?? string.Empty);
            csv.WriteField(contentCheck?.IsSpam.ToString() ?? "Not Checked");
            csv.WriteField(contentCheck?.Confidence.ToString() ?? string.Empty);
            csv.WriteField(contentCheck?.Reason ?? string.Empty);
            csv.WriteField(contentCheck?.CheckType ?? string.Empty);
            await csv.NextRecordAsync();
        }

        await writer.FlushAsync();
        var result = memoryStream.ToArray();

        // Audit log the export
        await auditService.LogEventAsync(
            AuditEventType.MessageExported,
            actor: userId != null ? Actor.FromWebUser(userId) : Actor.Unknown,
            target: null,
            value: $"Exported {messageCount} messages to CSV ({result.Length} bytes)",
            ct: ct);

        return result;
    }

    public async Task<byte[]> ExportToJsonAsync(
        IEnumerable<MessageRecord> messages,
        Dictionary<long, ContentCheckRecord?> contentChecks,
        CancellationToken ct = default)
    {
        // Validate permission (Admin+ required)
        var userId = await ValidateExportPermissionAsync();

        var messageList = messages.ToList();
        var messageCount = messageList.Count;

        var exportData = messageList.Select(message =>
        {
            var contentCheck = contentChecks.GetValueOrDefault(message.MessageId);
            var timestamp = message.Timestamp;

            return new
            {
                MessageId = message.MessageId,
                Timestamp = message.Timestamp,
                DateTime = timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                UserId = message.UserId,
                UserName = message.UserName,
                ChatId = message.ChatId,
                ChatName = message.ChatName,
                MessageText = message.MessageText,
                Urls = message.Urls?.Split(',', StringSplitOptions.RemoveEmptyEntries),
                HasImage = !string.IsNullOrEmpty(message.PhotoFileId),
                PhotoFileId = message.PhotoFileId,
                PhotoFileSize = message.PhotoFileSize,
                PhotoLocalPath = message.PhotoLocalPath,
                PhotoThumbnailPath = message.PhotoThumbnailPath,
                EditDate = message.EditDate,
                IsEdited = message.EditDate.HasValue,
                ContentHash = message.ContentHash,
                ContentCheck = contentCheck != null ? new
                {
                    IsSpam = contentCheck.IsSpam,
                    Confidence = contentCheck.Confidence,
                    Reason = contentCheck.Reason,
                    CheckType = contentCheck.CheckType,
                    CheckTimestamp = contentCheck.CheckTimestamp
                } : null
            };
        }).ToList();

        var json = JsonSerializer.Serialize(exportData, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var result = Encoding.UTF8.GetBytes(json);

        // Audit log the export
        await auditService.LogEventAsync(
            AuditEventType.MessageExported,
            actor: userId != null ? Actor.FromWebUser(userId) : Actor.Unknown,
            target: null,
            value: $"Exported {messageCount} messages to JSON ({result.Length} bytes)",
            ct: ct);

        return result;
    }
}
