using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Telegram.Abstractions.Jobs;

namespace TelegramGroupsAdmin.Telegram.Handlers;

/// <summary>
/// Handles detection of scannable file attachments and schedules malware scanning.
/// Only scans Document types (PDF, EXE, ZIP, Office files, etc.)
/// Excludes media files (Animation/GIF, Video, Audio, Voice, Sticker, VideoNote) which cannot contain executable malware.
/// Photos are handled separately via image spam detection (OpenAI Vision).
/// </summary>
public class FileScanningHandler
{
    private readonly IJobScheduler _jobScheduler;
    private readonly ILogger<FileScanningHandler> _logger;

    public FileScanningHandler(
        IJobScheduler jobScheduler,
        ILogger<FileScanningHandler> logger)
    {
        _jobScheduler = jobScheduler;
        _logger = logger;
    }

    /// <summary>
    /// Process message for file scanning: detect scannable documents and schedule background scan.
    /// Returns scan scheduling result if document found, null otherwise.
    /// </summary>
    public async Task<FileScanSchedulingResult?> ProcessFileScanningAsync(
        Message message,
        long chatId,
        long userId,
        CancellationToken cancellationToken = default)
    {
        var detection = DetectScannableFile(message);
        if (detection == null)
        {
            return null;
        }

        // Schedule file scan via Quartz.NET with 0s delay for instant execution
        // Phase 4.14: Downloads file to temp, scans with ClamAV+VirusTotal, deletes if infected
        // Temp file deleted after scan (no persistent storage)
        var scanPayload = new FileScanJobPayload(
            MessageId: message.MessageId,
            ChatId: chatId,
            UserId: userId,
            FileId: detection.FileId,
            FileSize: detection.FileSize,
            FileName: detection.FileName,
            ContentType: detection.ContentType
        );

        _logger.LogInformation(
            "Scheduling file scan for '{FileName}' ({FileSize} bytes) from user {UserId} in chat {ChatId}",
            detection.FileName ?? "unknown",
            detection.FileSize,
            userId,
            chatId);

        await _jobScheduler.ScheduleJobAsync(
            "FileScan",
            scanPayload,
            delaySeconds: 0,
            cancellationToken);

        return new FileScanSchedulingResult(
            FileId: detection.FileId,
            FileSize: detection.FileSize,
            FileName: detection.FileName,
            ContentType: detection.ContentType,
            Scheduled: true
        );
    }

    /// <summary>
    /// Detect scannable file attachment in message (Document type only).
    /// Returns null if no scannable document found.
    ///
    /// IMPORTANT: Telegram sends GIFs with BOTH Animation and Document properties set.
    /// We must check for media properties first to avoid scanning GIFs as documents.
    /// </summary>
    public static FileDetectionResult? DetectScannableFile(Message message)
    {
        // CRITICAL: Check if this is actually a media file BEFORE checking Document
        // Telegram populates BOTH Animation+Document for GIFs, Video+Document for videos, etc.
        // We only want to scan pure Document attachments (PDFs, executables, Office files)
        if (message.Animation != null ||
            message.Video != null ||
            message.Audio != null ||
            message.Voice != null ||
            message.Sticker != null ||
            message.VideoNote != null)
        {
            return null; // This is a media file, not a scannable document
        }

        // Only scan pure Document type (PDF, DOCX, EXE, ZIP, APK, etc.)
        // Media files cannot contain executable malware
        if (message.Document != null)
        {
            return new FileDetectionResult(
                FileId: message.Document.FileId,
                FileSize: message.Document.FileSize ?? 0,
                FileName: message.Document.FileName,
                ContentType: message.Document.MimeType
            );
        }

        return null;
    }
}

/// <summary>
/// Result of scannable file detection (pure metadata extraction)
/// </summary>
public record FileDetectionResult(
    string FileId,
    long FileSize,
    string? FileName,
    string? ContentType
);

/// <summary>
/// Result of file scan scheduling (detection + background job scheduling)
/// </summary>
public record FileScanSchedulingResult(
    string FileId,
    long FileSize,
    string? FileName,
    string? ContentType,
    bool Scheduled
);
