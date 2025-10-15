namespace TelegramGroupsAdmin.Models.Dialogs;

public class AddSpamSampleData
{
    public string SampleText { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
}

public class EditSpamSampleData
{
    public long Id { get; set; }
    public string SampleText { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
}

public class AddStopWordData
{
    public string Word { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

public class EditStopWordData
{
    public long Id { get; set; }
    public string? Notes { get; set; }
}

public class AddTrainingSampleData
{
    public string MessageText { get; set; } = string.Empty;
    public bool IsSpam { get; set; }
    public string Source { get; set; } = string.Empty;
    public int? Confidence { get; set; }
}

public class EditTrainingSampleData
{
    public long Id { get; set; }
    public string MessageText { get; set; } = string.Empty;
    public bool IsSpam { get; set; }
    public string Source { get; set; } = string.Empty;
    public int? Confidence { get; set; }
}

public class StopWordItem
{
    public long Id { get; set; }
    public string Word { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public DateTimeOffset AddedDate { get; set; }
    public string? AddedBy { get; set; }
    public string? Notes { get; set; }
}