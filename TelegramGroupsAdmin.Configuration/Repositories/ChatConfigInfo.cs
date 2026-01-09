namespace TelegramGroupsAdmin.Configuration.Repositories;

/// <summary>
/// Information about a chat's configuration
/// </summary>
public record ChatConfigInfo
{
    public long ChatId { get; init; }
    public string? ChatName { get; init; }
    public DateTimeOffset LastUpdated { get; init; }
    public string? UpdatedBy { get; init; }
    public bool HasCustomConfig { get; init; }
}
