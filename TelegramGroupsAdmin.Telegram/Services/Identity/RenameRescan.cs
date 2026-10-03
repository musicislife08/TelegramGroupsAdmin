namespace TelegramGroupsAdmin.Telegram.Services.Identity;

/// <summary>Whether ObserveAsync rescans the profile inline when it records a rename.</summary>
public enum RenameRescan
{
    None = 0,
    Inline = 1
}
