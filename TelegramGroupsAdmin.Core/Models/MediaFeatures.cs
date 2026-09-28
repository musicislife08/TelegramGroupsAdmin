namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// Perceptual-hash features of a message's media. Case types are self-contained so this base can
/// later become a C# 15 union without touching persistence (Data owns the JSON contract).
/// Readers switch over the case types; they never use base members.
/// </summary>
public abstract record MediaFeatures;

/// <summary>Perceptual hash of a photo.</summary>
public sealed record PhotoFeatures(byte[] Hash) : MediaFeatures;

/// <summary>Perceptual hashes of a video's extracted keyframes.</summary>
public sealed record VideoFeatures(IReadOnlyList<KeyframeFeature> Keyframes) : MediaFeatures;

/// <summary>Perceptual hash of one keyframe, at its relative position in the video (0.0-1.0).</summary>
public sealed record KeyframeFeature(double Position, byte[] Hash);
