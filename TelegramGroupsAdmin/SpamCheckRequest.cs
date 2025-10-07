using System.Text.Json.Serialization;

namespace TelegramGroupsAdmin;

public record SpamCheckRequest(
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("user_id")] string UserId,
    [property: JsonPropertyName("user_name")] string UserName,
    [property: JsonPropertyName("chat_id")] string ChatId,
    [property: JsonPropertyName("image_count")] int ImageCount
);