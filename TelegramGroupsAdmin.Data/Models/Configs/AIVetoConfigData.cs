namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of AIVetoConfig for EF Core JSON column mapping.
/// </summary>
public class AIVetoConfigData
{
    public bool UseGlobal { get; set; } = true;

    public bool Enabled { get; set; } = true;

    public bool CheckShortMessages { get; set; }

    // SystemPrompt removed - prompts are managed in prompt_versions table

    public int MessageHistoryCount { get; set; } = 3;

    public double ConfidenceThreshold { get; set; } = 4.25;

    public bool AlwaysRun { get; set; }
}
