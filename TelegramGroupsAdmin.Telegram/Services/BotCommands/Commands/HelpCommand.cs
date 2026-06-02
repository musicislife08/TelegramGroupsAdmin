using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;

/// <summary>
/// /help - Display available commands
/// </summary>
public class HelpCommand : IBotCommand
{
    private readonly IServiceProvider _serviceProvider;

    // Static metadata for all commands (avoids reflection complexity with DI)
    // Note: /start is excluded - it's only for deep links and DMs
    private static readonly List<CommandMetadata> _commandMetadata =
    [
        new("report", "Report message for admin review", 0),
        new("invite", "Get invite link for this chat", -1),
        new("link", "Link your Telegram account to web app", 1),
        new("spam", "Mark message as spam and delete it", 1),
        new("ban", "Ban user from all managed chats", 1),
        new("tempban", "Temporarily ban user with auto-unrestriction", 1),
        new("trust", "Whitelist user (bypass spam detection)", 1),
        new("unban", "Remove ban from user", 1),
        new("warn", "Issue warning to user", 1),
        new("delete", "[TEST] Delete a message", 1)
    ];

    private record CommandMetadata(string Name, string Description, int MinPermissionLevel);

    public HelpCommand(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

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

        // Group by permission level
        var readOnlyCommands = availableCommands.Where(c => c.MinPermissionLevel == 0).ToList();
        var adminCommands = availableCommands.Where(c => c.MinPermissionLevel >= 1).ToList();

        // Show Admin commands (including self)
        AppendCommandLine(builder, GetCommandEmoji("help"), "help", Description);

        foreach (var cmd in readOnlyCommands)
        {
            AppendCommandLine(builder, GetCommandEmoji(cmd.Name), cmd.Name, cmd.Description);
        }

        // Show Admin commands
        if (adminCommands.Any() && userPermission >= 1)
        {
            builder.LineBreak().Bold("Admin Commands:").LineBreak();
            foreach (var cmd in adminCommands)
            {
                AppendCommandLine(builder, GetCommandEmoji(cmd.Name), cmd.Name, cmd.Description);
            }
        }

        builder.LineBreak().Italic($"Permission: {GetPermissionName(userPermission)}");

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

    private static string GetPermissionName(int level) => level switch
    {
        0 => "Admin",
        1 => "GlobalAdmin",
        2 => "Owner",
        _ => "Unknown"
    };
}
