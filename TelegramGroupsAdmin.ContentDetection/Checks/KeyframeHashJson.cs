using System.Text.Json.Serialization;

namespace TelegramGroupsAdmin.ContentDetection.Checks;

/// <summary>
/// Keyframe hash JSON structure for deserialization from video_training_samples.keyframe_hashes
/// </summary>
internal record KeyframeHashJson(
    [property: JsonPropertyName("position")] double Position,
    [property: JsonPropertyName("hash")] string Hash
);
