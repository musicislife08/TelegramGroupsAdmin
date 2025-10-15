using System.Text;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;

/// <summary>
/// /help - Display available commands
/// </summary>
public class HelpCommand : IBotCommand
{
    private readonly IServiceProvider _serviceProvider;

    // Static metadata for all commands (avoids reflection complexity with DI)
    private static readonly List<CommandMetadata> _commandMetadata = new()
    {
        new("report", "Report message for admin review", 0),
        new("link", "Link your Telegram account to web app", 0),
        new("spam", "Mark message as spam and delete it", 1),
        new("ban", "Ban user from all managed chats", 1),
        new("trust", "Whitelist user (bypass spam detection)", 1),
        new("unban", "Remove ban from user", 1),
        new("warn", "Issue warning to user", 1),
        new("delete", "[TEST] Delete a message", 1)
    };

    private record CommandMetadata(string Name, string Description, int MinPermissionLevel);

    public HelpCommand(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public string Name => "help";
    public string Description => "Show available commands";
    public string Usage => "/help";
    public int MinPermissionLevel => 0; // Everyone can see help
    public bool RequiresReply => false;
    public bool DeleteCommandMessage => false; // Keep visible for reference

    public Task<string> ExecuteAsync(
        ITelegramBotClient botClient,
        Message message,
        string[] args,
        int userPermissionLevel,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine("🤖 *TelegramGroupsAdmin Bot*\n");

        var availableCommands = _commandMetadata
            .Where(c => c.MinPermissionLevel <= userPermissionLevel)
            .ToList();

        // Group by permission level
        var readOnlyCommands = availableCommands.Where(c => c.MinPermissionLevel == 0).ToList();
        var adminCommands = availableCommands.Where(c => c.MinPermissionLevel >= 1).ToList();

        // Show ReadOnly commands (including self)
        sb.AppendLine($"{GetCommandEmoji("help")} `/help` - {Description}");

        foreach (var cmd in readOnlyCommands)
        {
            var emoji = GetCommandEmoji(cmd.Name);
            sb.AppendLine($"{emoji} `/{cmd.Name}` - {cmd.Description}");
        }

        // Show Admin commands
        if (adminCommands.Any() && userPermissionLevel >= 1)
        {
            sb.AppendLine("\n*Admin Commands:*");
            foreach (var cmd in adminCommands)
            {
                var emoji = GetCommandEmoji(cmd.Name);
                sb.AppendLine($"{emoji} `/{cmd.Name}` - {cmd.Description}");
            }
        }

        sb.AppendLine($"\n_Permission: {GetPermissionName(userPermissionLevel)}_");

        return Task.FromResult(sb.ToString());
    }

    private static string GetCommandEmoji(string commandName) => commandName switch
    {
        "help" => "📋",
        "report" => "📢",
        "spam" => "🚫",
        "ban" => "⛔",
        "trust" => "✅",
        "unban" => "🔓",
        "warn" => "⚠️",
        _ => "🔹"
    };

    private static string GetPermissionName(int level) => level switch
    {
        0 => "ReadOnly",
        1 => "Admin",
        2 => "Owner",
        _ => "Unknown"
    };
}
