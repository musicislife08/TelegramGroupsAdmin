using System.Text;
using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Services.BotCommands.Commands;

/// <summary>
/// /help - Display available commands
/// </summary>
public class HelpCommand : IBotCommand
{
    public string Name => "help";
    public string Description => "Show available commands";
    public string Usage => "/help";
    public int MinPermissionLevel => 0; // Everyone can see help
    public bool RequiresReply => false;

    public Task<string> ExecuteAsync(
        Message message,
        string[] args,
        int userPermissionLevel,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine("🤖 *TelegramGroupsAdmin Bot*\n");

        // Hardcoded command list to avoid circular dependency
        // TODO: Make this dynamic when we solve the DI pattern
        if (userPermissionLevel >= 0)
        {
            sb.AppendLine("📋 `/help` - Show available commands");
        }

        if (userPermissionLevel >= 1) // Admin
        {
            sb.AppendLine("🚫 `/spam` - Mark message as spam (reply to message)");
        }

        sb.AppendLine($"\n_Permission: {GetPermissionName(userPermissionLevel)}_");

        return Task.FromResult(sb.ToString());
    }

    private static string GetPermissionName(int level) => level switch
    {
        0 => "ReadOnly",
        1 => "Admin",
        2 => "Owner",
        _ => "Unknown"
    };
}
