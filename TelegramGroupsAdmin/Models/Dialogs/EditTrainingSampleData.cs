namespace TelegramGroupsAdmin.Models.Dialogs;

public class EditTrainingSampleData
{
    public long Id { get; set; }
    public string MessageText { get; set; } = string.Empty;
    public bool IsSpam { get; set; }
    public string Source { get; set; } = string.Empty;
    public int? Confidence { get; set; }
}
