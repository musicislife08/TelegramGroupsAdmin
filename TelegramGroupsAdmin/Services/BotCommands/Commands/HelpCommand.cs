using System.Reflection;
using System.Text;
using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Services.BotCommands.Commands;

/// <summary>
/// /help - Display available commands (uses reflection to discover commands)
/// </summary>
public class HelpCommand : IBotCommand
{
    private static readonly Lazy<List<CommandMetadata>> _commandMetadata = new(() =>
    {
        // Use reflection to find all IBotCommand implementations and extract metadata
        return Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IBotCommand).IsAssignableFrom(t))
            .Where(t => t != typeof(HelpCommand)) // Exclude self to avoid circular reference
            .Select(t =>
            {
                // Create instance with null logger (only reading properties, not executing)
                var instance = (IBotCommand)Activator.CreateInstance(t, args: new object[] { null! })!;

                return new CommandMetadata(
                    instance.Name,
                    instance.Description,
                    instance.MinPermissionLevel);
            })
            .OrderBy(c => c.MinPermissionLevel)
            .ThenBy(c => c.Name)
            .ToList();
    });

    private record CommandMetadata(string Name, string Description, int MinPermissionLevel);

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

        var availableCommands = _commandMetadata.Value
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
