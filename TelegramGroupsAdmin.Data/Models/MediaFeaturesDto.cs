using System.Text.Json.Serialization;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// Persisted media_features JSON contract: always carries "type"; each case has a unique required
/// property ("hash" / "keyframes"). Do not change field names: stored rows depend on them.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(PhotoFeaturesDto), "photo")]
[JsonDerivedType(typeof(VideoFeaturesDto), "video")]
public abstract record MediaFeaturesDto;

public sealed record PhotoFeaturesDto(
    [property: JsonPropertyName("hash"), JsonRequired] byte[] Hash) : MediaFeaturesDto;

public sealed record VideoFeaturesDto(
    [property: JsonPropertyName("keyframes"), JsonRequired] IReadOnlyList<KeyframeFeatureDto> Keyframes) : MediaFeaturesDto;

public sealed record KeyframeFeatureDto(
    [property: JsonPropertyName("position")] double Position,
    [property: JsonPropertyName("hash"), JsonRequired] byte[] Hash);
