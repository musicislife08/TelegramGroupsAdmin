namespace TelegramGroupsAdmin.Configuration.Models;

/// <summary>
/// AI feature types that can be configured independently
/// </summary>
public enum AIFeatureType
{
    /// <summary>
    /// Text spam analysis
    /// </summary>
    SpamDetection,

    /// <summary>
    /// Message translation
    /// </summary>
    Translation,

    /// <summary>
    /// Vision API for images
    /// </summary>
    ImageAnalysis,

    /// <summary>
    /// Vision API for video frames
    /// </summary>
    VideoAnalysis,

    /// <summary>
    /// Meta-AI prompt generation
    /// </summary>
    PromptBuilder,

    /// <summary>
    /// Vision API for user profile scanning (bio, photos, stories)
    /// </summary>
    ProfileScan
}
