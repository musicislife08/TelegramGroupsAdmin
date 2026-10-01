using System.Text.Json;
using System.Text.Json.Serialization;

namespace TelegramGroupsAdmin.Core.Utilities;

/// <summary>
/// Reads a <c>string[]</c> property that older rows stored as one comma-separated string
/// (e.g. <c>reports.context.aiSignals</c> written before the context carried an array).
/// A JSON array reads as-is; a JSON string is split on commas with blanks dropped; null stays null.
/// Always writes the array shape.
/// </summary>
public sealed class CommaSeparatedOrArrayJsonConverter : JsonConverter<string[]?>
{
    public override string[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.String:
                return reader.GetString()!
                    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            case JsonTokenType.StartArray:
                var items = new List<string>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (reader.TokenType != JsonTokenType.String)
                        throw new JsonException($"Expected a string array element but found {reader.TokenType}.");
                    items.Add(reader.GetString()!);
                }
                return [.. items];
            default:
                throw new JsonException($"Expected a string array or a comma-separated string but found {reader.TokenType}.");
        }
    }

    public override void Write(Utf8JsonWriter writer, string[]? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartArray();
        foreach (var item in value)
        {
            writer.WriteStringValue(item);
        }
        writer.WriteEndArray();
    }
}
