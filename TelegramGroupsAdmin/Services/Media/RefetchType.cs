namespace TelegramGroupsAdmin.Services.Media;

/// <summary>
/// Type of refetch operation requested
/// </summary>
public enum RefetchType
{
    /// <summary>Media file (video, audio, sticker, etc.)</summary>
    Media,

    /// <summary>User profile photo</summary>
    UserPhoto
}
