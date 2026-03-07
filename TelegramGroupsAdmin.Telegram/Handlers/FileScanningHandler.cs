using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models.ContentDetection;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.JobPayloads;
using TelegramGroupsAdmin.Core.Models;
using static TelegramGroupsAdmin.Core.BackgroundJobs.DeduplicationKeys;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Handlers;

/// <summary>
/// Handles detection of scannable file attachments and schedules malware scanning.
/// Only scans Document types (PDF, EXE, ZIP, Office files, etc.)
/// Excludes media files (Animation/GIF, Video, Audio, Voice, Sticker, VideoNote) which cannot contain executable malware.
/// Photos are handled separately via image spam detection (OpenAI Vision).
///
/// Respects ContentDetectionConfig.FileScanning settings:
/// - Enabled: If false, file scanning is completely disabled
/// - AlwaysRun: If true, scans files even for trusted/admin users (security critical check)
/// - UseGlobal: If true, uses global config; if false, uses chat-specific overrides
/// </summary>
public class FileScanningHandler
{
    private readonly IJobScheduler _jobScheduler;
    private readonly IConfigService _configService;
    private readonly ITelegramUserRepository _userRepository;
    private readonly IChatAdminsRepository _chatAdminsRepository;
    private readonly ILogger<FileScanningHandler> _logger;

    public FileScanningHandler(
        IJobScheduler jobScheduler,
        IConfigService configService,
        ITelegramUserRepository userRepository,
        IChatAdminsRepository chatAdminsRepository,
        ILogger<FileScanningHandler> logger)
    {
        _jobScheduler = jobScheduler;
        _configService = configService;
        _userRepository = userRepository;
        _chatAdminsRepository = chatAdminsRepository;
        _logger = logger;
    }

    /// <summary>
    /// Process message for file scanning: detect scannable documents and schedule background scan.
    /// Returns scan scheduling result if document found, null otherwise.
    /// Respects FileScanning.Enabled and FileScanning.AlwaysRun configuration.
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

        // Get effective content detection config for this chat
        var config = await _configService.GetEffectiveAsync<ContentDetectionConfig>(ConfigType.ContentDetection, chatId);

        // If no config exists, use defaults (file scanning enabled with AlwaysRun=true)
        var fileScanningConfig = config?.FileScanning ?? new FileScanningDetectionConfig();

        // Check if file scanning is enabled
        if (!fileScanningConfig.Enabled)
        {
            _logger.LogInformation(
                "File scanning disabled for {Chat}, skipping scan for '{FileName}'",
                message.Chat.ToLogInfo(),
                detection.FileName ?? "unknown");

            return new FileScanSchedulingResult(
                FileId: detection.FileId,
                FileSize: detection.FileSize,
                FileName: detection.FileName,
                ContentType: detection.ContentType,
                Scheduled: false,
                SkipReason: "File scanning is disabled for this chat");
        }

        // Check trust/admin status for AlwaysRun bypass logic
        if (!fileScanningConfig.AlwaysRun)
        {
            var isUserTrusted = await _userRepository.IsTrustedAsync(userId, cancellationToken);
            var isUserAdmin = await _chatAdminsRepository.IsAdminAsync(chatId, userId, cancellationToken);

            if (isUserTrusted || isUserAdmin)
            {
                var skipReason = isUserTrusted
                    ? "User is trusted and FileScanning.AlwaysRun is disabled"
                    : "User is admin and FileScanning.AlwaysRun is disabled";

                _logger.LogInformation(
                    "Skipping file scan for '{FileName}' from {User} in {Chat}: {Reason}",
                    detection.FileName ?? "unknown",
                    message.From.ToLogInfo(),
                    message.Chat.ToLogInfo(),
                    skipReason);

                return new FileScanSchedulingResult(
                    FileId: detection.FileId,
                    FileSize: detection.FileSize,
                    FileName: detection.FileName,
                    ContentType: detection.ContentType,
                    Scheduled: false,
                    SkipReason: skipReason);
            }
        }

        // Schedule file scan via Quartz.NET with 0s delay for instant execution
        // Phase 4.14: Downloads file to temp, scans with ClamAV+VirusTotal, deletes if infected
        // Temp file deleted after scan (no persistent storage)
        var scanPayload = new FileScanJobPayload(
            MessageId: message.MessageId,
            Chat: ChatIdentity.From(message.Chat),
            User: message.From != null ? UserIdentity.From(message.From) : UserIdentity.FromId(userId),
            FileId: detection.FileId,
            FileSize: detection.FileSize,
            FileName: detection.FileName,
            ContentType: detection.ContentType
        );

        _logger.LogInformation(
            "Scheduling file scan for '{FileName}' ({FileSize} bytes) from {User} in {Chat}",
            detection.FileName ?? "unknown",
            detection.FileSize,
            message.From.ToLogInfo(),
            message.Chat.ToLogInfo());

        await _jobScheduler.ScheduleJobAsync(
            "FileScan",
            scanPayload,
            delaySeconds: 0,
            deduplicationKey: None,
            cancellationToken);

        return new FileScanSchedulingResult(
            FileId: detection.FileId,
            FileSize: detection.FileSize,
            FileName: detection.FileName,
            ContentType: detection.ContentType,
            Scheduled: true,
            SkipReason: null);
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
    bool Scheduled,
    string? SkipReason = null
);
