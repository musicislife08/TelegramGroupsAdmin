namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Aggregate response for the Register page initialization.
/// </summary>
public record RegisterPageResponse(
    bool IsFirstRun,
    bool IsEmailVerificationEnabled);
