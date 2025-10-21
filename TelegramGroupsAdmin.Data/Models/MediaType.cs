namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// Type of media attachment in a message (Database DTO enum)
/// Phase 4.X: Media attachment support
/// NOTE: This enum is duplicated in TelegramGroupsAdmin.Telegram.Models.MediaType (UI layer)
/// Keep values synchronized between both enums - conversion happens in repository layer
/// </summary>
public enum MediaType
{
    /// <summary>No media attachment</summary>
    None = 0,

    /// <summary>Animated GIF (Telegram Animation type)</summary>
    Animation = 1,

    /// <summary>Video file</summary>
    Video = 2,

    /// <summary>Audio file (music)</summary>
    Audio = 3,

    /// <summary>Voice message</summary>
    Voice = 4,

    /// <summary>Sticker (WebP format)</summary>
    Sticker = 5,

    /// <summary>Video note (circular video message)</summary>
    VideoNote = 6,

    /// <summary>Document file (PDF, DOCX, ZIP, etc.)</summary>
    Document = 7
}
