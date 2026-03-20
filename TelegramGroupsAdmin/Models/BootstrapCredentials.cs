using System.Text.Json.Serialization;

namespace TelegramGroupsAdmin.Models;

internal sealed record BootstrapCredentials(
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("password")] string? Password
);
