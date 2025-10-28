namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// DTO for invite with creator and used-by emails (used in JOIN queries)
/// Not an EF entity - just a DTO for query results
/// </summary>
public class InviteWithCreatorDto
{
    public InviteRecordDto Invite { get; set; } = null!;
    public string CreatorEmail { get; set; } = string.Empty;
    public string? UsedByEmail { get; set; }
}
