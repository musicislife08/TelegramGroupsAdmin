using System.Text.Json;
using System.Text.Json.Serialization;

namespace TelegramGroupsAdmin.BackgroundJobs.Services.Backup;

/// <summary>
/// JSON converter for BackupMetadata.CreatedAt that handles both legacy Unix timestamp (long)
/// and modern ISO 8601 DateTimeOffset format for backward compatibility.
///
/// Serialization: Always writes ISO 8601 string (new format)
/// Deserialization: Accepts both Unix timestamp (legacy) and ISO 8601 string (new)
/// </summary>
public class BackupMetadataDateTimeConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                // Legacy format: Unix timestamp in seconds
                var unixTimestamp = reader.GetInt64();
                return DateTimeOffset.FromUnixTimeSeconds(unixTimestamp);

            case JsonTokenType.String:
                // New format: ISO 8601 string
                var dateString = reader.GetString();
                if (DateTimeOffset.TryParse(dateString, out var parsedDate))
                {
                    return parsedDate;
                }
                throw new JsonException($"Unable to parse '{dateString}' as DateTimeOffset");

            default:
                throw new JsonException($"Unexpected token type {reader.TokenType} when parsing DateTimeOffset");
        }
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        // Always write as ISO 8601 string (new format)
        writer.WriteStringValue(value.ToString("O")); // "O" = round-trip date/time pattern
    }
}
