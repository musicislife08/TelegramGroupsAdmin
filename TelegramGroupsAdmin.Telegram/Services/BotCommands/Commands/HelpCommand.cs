using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;

/// <summary>
/// /help - Display available commands
/// </summary>
public class HelpCommand : IBotCommand
{
    // Static metadata for all commands (avoids reflection complexity with DI)
    // Note: /start is excluded - it's only for deep links and DMs
    private static readonly List<CommandMetadata> _commandMetadata =
    [
        new("report", "Report message for admin review", PermissionLevel.Member),
        new("invite", "Get invite link for this chat", PermissionLevel.Member),
        new("link", "Link your Telegram account to web app", PermissionLevel.Member),
        new("spam", "Mark message as spam and delete it", PermissionLevel.Admin),
        new("ban", "Ban user from all managed chats", PermissionLevel.Admin),
        new("tempban", "Temporarily ban user with auto-unrestriction", PermissionLevel.Admin),
        new("trust", "Whitelist user (bypass spam detection)", PermissionLevel.Admin),
        new("unban", "Remove ban from user", PermissionLevel.Admin),
        new("warn", "Issue warning to user", PermissionLevel.Admin),
        new("delete", "[TEST] Delete a message", PermissionLevel.Admin)
    ];

    private record CommandMetadata(string Name, string Description, PermissionLevel MinPermissionLevel);

    public string Name => "help";
    public string Description => "Show available commands";
    public string Usage => "/help";
    public PermissionLevel MinPermissionLevel => PermissionLevel.Member; // everyone
    public bool RequiresReply => false;
    public bool DeleteCommandMessage => false; // Keep visible for reference
    public int? DeleteResponseAfterSeconds => 30; // Auto-delete help response after 30 seconds

    public Task<CommandResult> ExecuteAsync(
        Message message,
        string[] args,
        PermissionLevel userPermission,
        CancellationToken cancellationToken = default)
    {
        var builder = new TelegramMessageBuilder()
            .Text("🤖 ").Bold("TelegramGroupsAdmin Bot").LineBreak()
            .LineBreak();

        var availableCommands = _commandMetadata
            .Where(c => c.MinPermissionLevel <= userPermission)
            .ToList();

        var publicCommands = availableCommands.Where(c => c.MinPermissionLevel < PermissionLevel.Admin).ToList();
        var adminCommands = availableCommands.Where(c => c.MinPermissionLevel >= PermissionLevel.Admin).ToList();

        // Show /help itself plus the public commands
        AppendCommandLine(builder, GetCommandEmoji("help"), "help", Description);
        foreach (var cmd in publicCommands)
        {
            AppendCommandLine(builder, GetCommandEmoji(cmd.Name), cmd.Name, cmd.Description);
        }

        // Show Admin commands only to admins
        if (adminCommands.Any() && userPermission >= PermissionLevel.Admin)
        {
            builder.LineBreak().Bold("Admin Commands:").LineBreak();
            foreach (var cmd in adminCommands)
            {
                AppendCommandLine(builder, GetCommandEmoji(cmd.Name), cmd.Name, cmd.Description);
            }
        }

        builder.LineBreak().Italic($"Permission: {userPermission.GetDisplayName()}");

        return Task.FromResult(new CommandResult(builder.Build(), DeleteCommandMessage, DeleteResponseAfterSeconds));
    }

    private static void AppendCommandLine(TelegramMessageBuilder builder, string emoji, string command, string description)
    {
        builder
            .Text($"{emoji} ").Code($"/{command}").Text($" - {description}")
            .LineBreak();
    }

    private static string GetCommandEmoji(string commandName) => commandName switch
    {
        "help" => "📋",
        "start" => "👋",
        "report" => "📢",
        "invite" => "🔗",
        "spam" => "🚫",
        "ban" => "⛔",
        "tempban" => "⏱️",
        "trust" => "✅",
        "unban" => "🔓",
        "warn" => "⚠️",
        _ => "🔹"
    };
}
