namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of UserApiConfig for EF Core JSON column mapping.
/// Maps to business model via ToModel/ToDto extensions in Configuration project.
/// </summary>
public class UserApiConfigData
{
    /// <summary>
    /// Telegram API ID from my.telegram.org
    /// </summary>
    public int ApiId { get; set; }
}
