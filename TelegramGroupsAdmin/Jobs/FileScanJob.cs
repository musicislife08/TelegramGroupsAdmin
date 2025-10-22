using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Models;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.ContentDetection.Abstractions;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Abstractions.Jobs;
using TelegramGroupsAdmin.Telegram.Abstractions.Services;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Jobs;

/// <summary>
/// TickerQ job to scan file attachments for malware using two-tier architecture:
/// - Tier 1: ClamAV (local, fast, unlimited)
/// - Tier 2: VirusTotal (cloud, slower, quota-limited)
///
/// Phase 4.14: Critical Checks Infrastructure
/// Runs asynchronously after message save with 0s delay for instant scanning
/// Provides persistence, retry logic for transient failures (ClamAV restart, VT rate limit)
/// If infected: Deletes message, DMs user (or fallback to chat reply), logs to audit
/// </summary>
public class FileScanJob(
    ILogger<FileScanJob> logger,
    TelegramBotClientFactory botClientFactory,
    IEnumerable<IContentCheck> contentChecks,
    ITelegramUserRepository telegramUserRepository,
    IMessageHistoryRepository messageHistoryRepository,
    IDetectionResultsRepository detectionResultsRepository,
    IOptions<TelegramOptions> telegramOptions)
{
    private readonly ILogger<FileScanJob> _logger = logger;
    private readonly TelegramBotClientFactory _botClientFactory = botClientFactory;
    private readonly IContentCheck _fileScanningCheck = contentChecks.First(c => c.CheckName == "FileScanning");
    private readonly ITelegramUserRepository _telegramUserRepository = telegramUserRepository;
    private readonly IMessageHistoryRepository _messageHistoryRepository = messageHistoryRepository;
    private readonly IDetectionResultsRepository _detectionResultsRepository = detectionResultsRepository;
    private readonly TelegramOptions _telegramOptions = telegramOptions.Value;

    /// <summary>
    /// Download file, scan for malware, take action if infected
    /// Scheduled via TickerQ with 0s delay for instant execution
    /// </summary>
    [TickerFunction(functionName: "FileScan")]
    public async Task ExecuteAsync(TickerFunctionContext<FileScanJobPayload> context, CancellationToken cancellationToken)
    {
        var payload = context.Request;
        if (payload == null)
        {
            _logger.LogError("FileScanJob received null payload");
            return;
        }

        _logger.LogInformation(
            "Scanning file '{FileName}' ({FileSize} bytes) from user {UserId} in chat {ChatId} (message {MessageId})",
            payload.FileName ?? "unknown",
            payload.FileSize,
            payload.UserId,
            payload.ChatId,
            payload.MessageId);

        // Get bot client from factory (singleton instance, one per bot token)
        var botClient = _botClientFactory.GetOrCreate(_telegramOptions.BotToken);

        string? tempFilePath = null;

        try
        {
            // Step 1: Download file from Telegram to temporary location
            tempFilePath = Path.Combine(Path.GetTempPath(), $"tg_scan_{Guid.NewGuid():N}_{payload.FileName ?? "file"}");

            _logger.LogDebug("Downloading file {FileId} to {TempPath}", payload.FileId, tempFilePath);

            // First get file info to get the file path
            var fileInfo = await botClient.GetFile(payload.FileId, cancellationToken);

            if (fileInfo.FilePath == null)
            {
                _logger.LogError("File path is null for file ID {FileId}", payload.FileId);
                return; // Can't download without file path
            }

            // Download file to temp location
            await using (var fileStream = File.Create(tempFilePath))
            {
                await botClient.DownloadFile(fileInfo.FilePath, fileStream, cancellationToken);
            }

            // Step 2: Calculate SHA256 hash for cache lookup and audit trail
            string fileHash;
            await using (var fileStream = File.OpenRead(tempFilePath))
            {
                fileHash = await HashUtilities.ComputeSHA256Async(fileStream, cancellationToken);
            }

            _logger.LogDebug("File hash: {FileHash}", fileHash);

            // Step 3: Read file bytes for scanning
            byte[] fileBytes = await File.ReadAllBytesAsync(tempFilePath, cancellationToken);

            // Step 4: Get user info for request
            var user = await _telegramUserRepository.GetByTelegramIdAsync(payload.UserId, cancellationToken);

            // Step 5: Create scan request and execute
            var scanRequest = new FileScanCheckRequest
            {
                Message = $"File attachment: {payload.FileName ?? "unknown"}",
                UserId = payload.UserId,
                UserName = user?.Username,
                ChatId = payload.ChatId,
                FileBytes = fileBytes,
                FileName = payload.FileName ?? "unknown",
                FileSize = payload.FileSize,
                FileHash = fileHash,
                CancellationToken = cancellationToken
            };

            var scanResult = await _fileScanningCheck.CheckAsync(scanRequest);

            _logger.LogInformation(
                "File scan complete for message {MessageId}: Result={Result}, Confidence={Confidence}, Details={Details}",
                payload.MessageId,
                scanResult.Result,
                scanResult.Confidence,
                scanResult.Details);

            // Step 5: Save detection history (for both clean and infected files)
            var detectionRecord = new Telegram.Models.DetectionResultRecord
            {
                MessageId = payload.MessageId,
                UserId = payload.UserId,
                DetectedAt = DateTimeOffset.UtcNow,
                DetectionSource = "file_scan", // Phase 4.14
                DetectionMethod = "FileScanningCheck",
                IsSpam = scanResult.Result == CheckResultType.Spam,
                Confidence = scanResult.Confidence,
                Reason = scanResult.Details,
                NetConfidence = scanResult.Result == CheckResultType.Spam ? scanResult.Confidence : -scanResult.Confidence,
                CheckResultsJson = null, // File scanning is a single check, no aggregation
                UsedForTraining = false, // File scans don't train spam detection
                MessageText = $"File: {payload.FileName ?? "unknown"} ({payload.FileSize} bytes)",
                AddedBy = Core.Models.Actor.FileScanner
            };

            await _detectionResultsRepository.InsertAsync(detectionRecord, cancellationToken);

            _logger.LogInformation(
                "Created detection history record for file scan: message {MessageId}, result={Result}",
                payload.MessageId,
                scanResult.Result);

            // Step 6: Take action if file is infected
            if (scanResult.Result == CheckResultType.Spam) // "Spam" = Infected for file scanning
            {
                await HandleInfectedFileAsync(botClient, payload, scanResult, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to scan file for message {MessageId} (user {UserId}, chat {ChatId})",
                payload.MessageId,
                payload.UserId,
                payload.ChatId);

            // Re-throw to let TickerQ handle retry logic
            // Retriable scenarios: ClamAV daemon restart, VirusTotal rate limit, network timeout
            throw;
        }
        finally
        {
            // Step 6: Clean up temporary file
            if (tempFilePath != null && File.Exists(tempFilePath))
            {
                try
                {
                    File.Delete(tempFilePath);
                    _logger.LogDebug("Deleted temporary file {TempPath}", tempFilePath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete temporary file {TempPath}", tempFilePath);
                }
            }
        }
    }

    /// <summary>
    /// Handle infected file: delete message, DM user (or fallback to chat reply), log to audit
    /// Policy: Delete + DM notice for ALL users (trusted/admin included per Phase 4.14)
    /// No ban/warn for trusted/admin users (handled by ContentActionService in future)
    /// </summary>
    private async Task HandleInfectedFileAsync(
        ITelegramBotClient botClient,
        FileScanJobPayload payload,
        ContentCheckResponse scanResult,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "🦠 INFECTED FILE DETECTED: User {UserId} in chat {ChatId}, message {MessageId} - {Details}",
            payload.UserId,
            payload.ChatId,
            payload.MessageId,
            scanResult.Details);

        try
        {
            // Delete the message containing infected file
            await botClient.DeleteMessage(payload.ChatId, (int)payload.MessageId, cancellationToken);

            _logger.LogInformation(
                "Deleted infected file message {MessageId} from chat {ChatId}",
                payload.MessageId,
                payload.ChatId);

            // Mark message as deleted in database for audit trail
            await _messageHistoryRepository.MarkMessageAsDeletedAsync(
                payload.MessageId,
                "file_scan_infected",
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to delete infected file message {MessageId} in chat {ChatId}",
                payload.MessageId,
                payload.ChatId);
        }

        // Notify user via DM (or fallback to chat if DM blocked)
        await NotifyUserAsync(botClient, payload, scanResult, cancellationToken);
    }

    /// <summary>
    /// Send DM to user about infected file, fallback to chat reply if DM blocked
    /// Phase 4.14: Implements bot_dm_enabled check and fallback pattern
    /// </summary>
    private async Task NotifyUserAsync(
        ITelegramBotClient botClient,
        FileScanJobPayload payload,
        ContentCheckResponse scanResult,
        CancellationToken cancellationToken)
    {
        var notificationText = $"⚠️ **Malware Detected**\n\n" +
            $"Your file `{payload.FileName ?? "unknown"}` was detected as malware and has been removed from the chat.\n\n" +
            $"**Detection Details:**\n{scanResult.Details}\n\n" +
            $"If you believe this is a false positive, please contact the chat administrators.";

        try
        {
            // Check if user has DM enabled (set when they /start the bot)
            var telegramUser = await _telegramUserRepository.GetByTelegramIdAsync(payload.UserId, cancellationToken);
            var canSendDm = telegramUser?.BotDmEnabled ?? false;

            if (canSendDm)
            {
                // Try to send DM
                try
                {
                    await botClient.SendMessage(
                        chatId: payload.UserId,
                        text: notificationText,
                        parseMode: ParseMode.Markdown,
                        cancellationToken: cancellationToken);

                    _logger.LogInformation(
                        "Sent malware notification DM to user {UserId}",
                        payload.UserId);
                    return; // Success - don't fallback
                }
                catch (Exception dmEx)
                {
                    _logger.LogWarning(
                        dmEx,
                        "Failed to send DM to user {UserId}, falling back to chat reply",
                        payload.UserId);

                    // Update bot_dm_enabled to false (user blocked bot)
                    await _telegramUserRepository.SetBotDmEnabledAsync(payload.UserId, false, cancellationToken);
                }
            }

            // Fallback: Send message in chat (mentions user)
            var chatNotificationText = $"⚠️ @{telegramUser?.Username ?? payload.UserId.ToString()}: {notificationText}";

            await botClient.SendMessage(
                chatId: payload.ChatId,
                text: chatNotificationText,
                parseMode: ParseMode.Markdown,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Sent malware notification in chat {ChatId} (DM not available for user {UserId})",
                payload.ChatId,
                payload.UserId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to notify user {UserId} about infected file (both DM and chat reply failed)",
                payload.UserId);
        }
    }
}
