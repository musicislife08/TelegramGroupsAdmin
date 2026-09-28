namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// What caused a verdict event (one detection_results row). Stored as int by the Data
/// layer: values are explicit and must never be renumbered.
/// </summary>
public enum VerdictSource
{
    /// <summary>Content detection engine scan of a message version (new message or edit).</summary>
    ContentScan = 0,
    /// <summary>Attachment scan. Never used as the message's verdict.</summary>
    FileScan = 1,
    /// <summary>High-confidence auto-ban or hard block.</summary>
    AutoBan = 10,
    WebMarkSpam = 11,
    WebMarkHam = 12,
    SpamCommand = 13,
    ReviewSpam = 14,
    /// <summary>Review queue "Dismiss": ham, but not an admin-grade label.</summary>
    ReviewDismiss = 15,
    TrainingDataPage = 16,
    /// <summary>"Remove from training": keeps spam/ham, drops training membership.</summary>
    TrainingExclude = 17,
    /// <summary>Bulk import (tg-spam).</summary>
    Import = 18,
    /// <summary>Migration only: a legacy manual row whose origin cannot be recovered.</summary>
    LegacyManual = 99
}
