namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// DTO for stop word with user email (used in JOIN queries)
/// Not an EF entity - just a DTO for query results
/// </summary>
public class StopWordWithEmailDto
{
    public StopWordDto StopWord { get; set; } = null!;
    public string? AddedByEmail { get; set; }
}
