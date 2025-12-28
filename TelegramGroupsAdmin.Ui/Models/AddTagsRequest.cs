namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Request to add tags to a user.
/// </summary>
public record AddTagsRequest(List<string> TagNames);
