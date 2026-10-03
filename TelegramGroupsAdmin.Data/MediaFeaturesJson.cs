using System.Text.Json;

namespace TelegramGroupsAdmin.Data;

/// <summary>Serializer options for the messages.media_features JSON contract.</summary>
public static class MediaFeaturesJson
{
    /// <summary>
    /// PostgreSQL jsonb reorders keys (shortest first), so a stored photo reads back with "type"
    /// after "hash"; the "type" discriminator must be accepted in any position.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        AllowOutOfOrderMetadataProperties = true
    };
}
