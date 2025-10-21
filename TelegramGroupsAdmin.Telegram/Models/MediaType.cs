namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Type of media attachment in a message
/// Phase 4.X: Media attachment support
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
