namespace TelegramGroupsAdmin.ContentDetection.Repositories;

/// <summary>
/// Represents a keyframe hash with its position in the video
/// Used for JSON serialization in video_training_samples.keyframe_hashes
/// </summary>
internal class KeyframeHash
{
    public double Position { get; set; }
    public string Hash { get; set; } = string.Empty;
}
