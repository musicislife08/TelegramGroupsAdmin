namespace TelegramGroupsAdmin.Ui.Server.Services.PromptBuilder;

/// <summary>
/// Strictness levels for spam detection
/// </summary>
public enum StrictnessLevel
{
    Conservative = 0, // Prefer false negatives (let spam through rather than block legit)
    Balanced = 1,     // Middle ground
    Aggressive = 2    // Prefer false positives (block questionable content)
}
