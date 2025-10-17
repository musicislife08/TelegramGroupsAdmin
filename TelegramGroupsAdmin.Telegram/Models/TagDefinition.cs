using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// UI model for tag definitions with color preferences
/// </summary>
public class TagDefinition
{
    public string TagName { get; set; } = string.Empty;
    public TagColor Color { get; set; } = TagColor.Primary;
    public int UsageCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
