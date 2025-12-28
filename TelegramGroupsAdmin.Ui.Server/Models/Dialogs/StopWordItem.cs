namespace TelegramGroupsAdmin.Ui.Server.Models.Dialogs;

public class StopWordItem
{
    public long Id { get; set; }
    public string Word { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public DateTimeOffset AddedDate { get; set; }
    public string? AddedBy { get; set; }
    public string? Notes { get; set; }
}
