namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Welcome message delivery mode
/// </summary>
public enum WelcomeMode
{
    /// <summary>Rules sent via DM with deep link button (clean group chat)</summary>
    DmWelcome = 0,

    /// <summary>Rules shown in chat with Accept/Deny buttons (faster onboarding)</summary>
    ChatAcceptDeny = 1
}
