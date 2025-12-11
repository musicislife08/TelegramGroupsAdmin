using System.Text.Json;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Quartz;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.ContentDetection.Abstractions;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.Telemetry;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Core.JobPayloads;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.BackgroundJobs.Jobs;

/// <summary>
/// Job logic to scan file attachments for malware using two-tier architecture:
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
    ITelegramBotClientFactory botClientFactory,
    IEnumerable<IContentCheckV2> contentChecks,
    ITelegramUserRepository telegramUserRepository,
    IMessageHistoryRepository messageHistoryRepository,
    IDetectionResultsRepository detectionResultsRepository) : IJob
{
    private readonly ILogger<FileScanJob> _logger = logger;
    private readonly ITelegramBotClientFactory _botClientFactory = botClientFactory;
    private readonly IContentCheckV2 _fileScanningCheck = contentChecks.First(c => c.CheckName == CheckName.FileScanning);
    private readonly ITelegramUserRepository _telegramUserRepository = telegramUserRepository;
    private readonly IMessageHistoryRepository _messageHistoryRepository = messageHistoryRepository;
    private readonly IDetectionResultsRepository _detectionResultsRepository = detectionResultsRepository;

    public async Task Execute(IJobExecutionContext context)
    {
        // Extract payload from job data map (deserialize from JSON string)
        var payloadJson = context.JobDetail.JobDataMap.GetString(JobDataKeys.PayloadJson)
            ?? throw new InvalidOperationException("payload not found in job data");

        var payload = JsonSerializer.Deserialize<FileScanJobPayload>(payloadJson)
            ?? throw new InvalidOperationException("Failed to deserialize FileScanJobPayload");

        await ExecuteAsync(payload, context.CancellationToken);
    }

    /// <summary>
    /// Download file, scan for malware, take action if infected
    /// Scheduled with 0s delay for instant execution
    /// </summary>
    private async Task ExecuteAsync(FileScanJobPayload payload, CancellationToken cancellationToken)
    {
        const string jobName = "FileScan";
        var startTimestamp = Stopwatch.GetTimestamp();
        var success = false;

        try
        {
            _logger.LogInformation(
                "Scanning file '{FileName}' ({FileSize} bytes) from user {UserId} in chat {ChatId} (message {MessageId})",
                payload.FileName ?? "unknown",
                payload.FileSize,
                payload.UserId,
                payload.ChatId,
                payload.MessageId);

            // Get operations from factory
            var operations = await _botClientFactory.GetOperationsAsync();

            string? tempFilePath = null;

            try
            {
                // Step 1: Download file from Telegram to temporary location
                tempFilePath = Path.Combine(Path.GetTempPath(), $"tg_scan_{Guid.NewGuid():N}_{payload.FileName ?? "file"}");

                _logger.LogDebug("Downloading file {FileId} to {TempPath}", payload.FileId, tempFilePath);

                // First get file info to get the file path
                var fileInfo = await operations.GetFileAsync(payload.FileId, cancellationToken);

                if (fileInfo.FilePath == null)
                {
                    _logger.LogError("File path is null for file ID {FileId}", payload.FileId);
                    return; // Can't download without file path
                }

                // Download file to temp location
                await using (var fileStream = File.Create(tempFilePath))
                {
                    await operations.DownloadFileAsync(fileInfo.FilePath, fileStream, cancellationToken);
                }

                // Step 2: Calculate SHA256 hash for cache lookup and audit trail
                string fileHash;
                await using (var fileStream = File.OpenRead(tempFilePath))
                {
                    fileHash = await HashUtilities.ComputeSHA256Async(fileStream, cancellationToken);
                }

                _logger.LogDebug("File hash: {FileHash}", fileHash);

                // Step 3: Get user info for request
                var user = await _telegramUserRepository.GetByTelegramIdAsync(payload.UserId, cancellationToken);

                // Step 4: Create scan request and execute
                // Phase 6: Pass file path instead of loading entire file into memory
                // Scanners will open their own streams to enable parallel scanning without memory duplication
                var scanRequest = new FileScanCheckRequest
                {
                    Message = $"File attachment: {payload.FileName ?? "unknown"}",
                    UserId = payload.UserId,
                    UserName = user?.Username,
                    ChatId = payload.ChatId,
                    FilePath = tempFilePath,
                    FileName = payload.FileName ?? "unknown",
                    FileSize = payload.FileSize,
                    FileHash = fileHash,
                    CancellationToken = cancellationToken
                };

                var scanResult = await _fileScanningCheck.CheckAsync(scanRequest);

                // V2 scoring: 5.0 = malware detected, 0.0 = clean/abstained
                // Map to V1 concepts: Score >= 4.0 = Infected, < 4.0 = Clean
                bool isInfected = !scanResult.Abstained && scanResult.Score >= 4.0;
                int confidenceV1 = (int)(scanResult.Score * 20); // Map 0-5.0 to 0-100

                _logger.LogInformation(
                    "File scan complete for message {MessageId}: Score={Score}, Abstained={Abstained}, Infected={Infected}, Details={Details}",
                    payload.MessageId,
                    scanResult.Score,
                    scanResult.Abstained,
                    isInfected,
                    scanResult.Details);

                // Step 5: Save detection history (for both clean and infected files)
                var detectionRecord = new DetectionResultRecord
                {
                    MessageId = payload.MessageId,
                    UserId = payload.UserId,
                    DetectedAt = DateTimeOffset.UtcNow,
                    DetectionSource = "file_scan", // Phase 4.14
                    DetectionMethod = "FileScanningCheck",
                    IsSpam = isInfected,
                    Confidence = confidenceV1,
                    Reason = scanResult.Details,
                    NetConfidence = isInfected ? confidenceV1 : -confidenceV1,
                    CheckResultsJson = null, // File scanning is a single check, no aggregation
                    UsedForTraining = false, // File scans don't train spam detection
                    MessageText = $"File: {payload.FileName ?? "unknown"} ({payload.FileSize} bytes)",
                    AddedBy = Core.Models.Actor.FileScanner
                };

                await _detectionResultsRepository.InsertAsync(detectionRecord, cancellationToken);

                _logger.LogInformation(
                    "Created detection history record for file scan: message {MessageId}, infected={Infected}",
                    payload.MessageId,
                    isInfected);

                // Step 6: Take action if file is infected
                if (isInfected)
                {
                    await HandleInfectedFileAsync(operations, payload, scanResult, cancellationToken);
                }
            }
            catch (ApiRequestException ex) when (ex.Message.Contains("file is too big"))
            {
                _logger.LogWarning(
                    "Skipping file scan for message {MessageId}: File '{FileName}' exceeds Telegram Bot API 20MB limit. " +
                    "File ID: {FileId}, Size: {FileSize} bytes. " +
                    "To scan files >20MB, configure self-hosted Bot API server (Settings → Telegram Bot → API Server URL).",
                    payload.MessageId,
                    payload.FileName ?? "unknown",
                    payload.FileId,
                    payload.FileSize);
                // Return without re-throwing - file scan skipped gracefully
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to scan file for message {MessageId} (user {UserId}, chat {ChatId})",
                    payload?.MessageId,
                    payload?.UserId,
                    payload?.ChatId);

                // Re-throw for retry logic and exception recording
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

            success = true;
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

            // Record metrics (using TagList to avoid boxing/allocations)
            var tags = new TagList
            {
                { "job_name", jobName },
                { "status", success ? "success" : "failure" }
            };

            TelemetryConstants.JobExecutions.Add(1, tags);
            TelemetryConstants.JobDuration.Record(elapsedMs, new TagList { { "job_name", jobName } });
        }
    }

    /// <summary>
    /// Handle infected file: delete message, DM user (or fallback to chat reply), log to audit
    /// Policy: Delete + DM notice for ALL users (trusted/admin included per Phase 4.14)
    /// No ban/warn for trusted/admin users (handled by ContentActionService in future)
    /// </summary>
    private async Task HandleInfectedFileAsync(
        ITelegramOperations operations,
        FileScanJobPayload payload,
        ContentCheckResponseV2 scanResult,
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
            await operations.DeleteMessageAsync(payload.ChatId, (int)payload.MessageId, cancellationToken);

            _logger.LogWarning(
                "Deleted infected file message {MessageId} from chat {ChatId}",
                payload.MessageId,
                payload.ChatId);

            // Mark message as deleted in database for audit trail
            await _messageHistoryRepository.MarkMessageAsDeletedAsync(
                payload.MessageId,
                "file_scan_infected",
                cancellationToken);
        }
        catch (Exception ex) when (
            ex.Message.Contains("message to delete not found") ||
            ex.Message.Contains("message can't be deleted") ||
            ex.Message.Contains("MESSAGE_DELETE_FORBIDDEN") ||
            ex.Message.Contains("Bad Request"))
        {
            // Expected errors: message already deleted by user/admin, or bot lacks permissions
            _logger.LogDebug(
                "Skipped deletion of message {MessageId} in chat {ChatId} (likely already deleted): {Reason}",
                payload.MessageId,
                payload.ChatId,
                ex.Message);

            // Still mark as deleted in DB for audit trail
            await _messageHistoryRepository.MarkMessageAsDeletedAsync(
                payload.MessageId,
                "file_scan_infected_already_deleted",
                cancellationToken);
        }
        catch (Exception ex)
        {
            // Unexpected error - log and continue (detection record is accurate, user will be notified)
            _logger.LogError(
                ex,
                "Unexpected error deleting infected file message {MessageId} in chat {ChatId}",
                payload.MessageId,
                payload.ChatId);
        }

        // Notify user via DM (or fallback to chat if DM blocked)
        await NotifyUserAsync(operations, payload, scanResult, cancellationToken);
    }

    /// <summary>
    /// Send DM to user about infected file, fallback to chat reply if DM blocked
    /// Phase 4.14: Implements bot_dm_enabled check and fallback pattern
    /// </summary>
    private async Task NotifyUserAsync(
        ITelegramOperations operations,
        FileScanJobPayload payload,
        ContentCheckResponseV2 scanResult,
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
                    await operations.SendMessageAsync(
                        chatId: payload.UserId,
                        text: notificationText,
                        parseMode: ParseMode.Markdown,
                        ct: cancellationToken);

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

            await operations.SendMessageAsync(
                chatId: payload.ChatId,
                text: chatNotificationText,
                parseMode: ParseMode.Markdown,
                ct: cancellationToken);

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
