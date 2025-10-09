using System.Text.RegularExpressions;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Services.BotCommands;

/// <summary>
/// Routes bot commands to appropriate handlers with permission checking
/// </summary>
public partial class CommandRouter
{
    private readonly ILogger<CommandRouter> _logger;
    private readonly Dictionary<string, IBotCommand> _commands;

    [GeneratedRegex(@"^/(\w+)(?:@\w+)?(?:\s+(.*))?$", RegexOptions.Compiled)]
    private static partial Regex CommandPattern();

    public CommandRouter(
        ILogger<CommandRouter> logger,
        IEnumerable<IBotCommand> commands)
    {
        _logger = logger;
        _commands = commands.ToDictionary(c => c.Name.ToLowerInvariant(), c => c);
    }

    /// <summary>
    /// Check if message contains a bot command
    /// </summary>
    public bool IsCommand(Message message)
    {
        if (message.Text == null) return false;

        var match = CommandPattern().Match(message.Text);
        return match.Success && _commands.ContainsKey(match.Groups[1].Value.ToLowerInvariant());
    }

    /// <summary>
    /// Route and execute bot command
    /// </summary>
    public async Task<string?> RouteCommandAsync(
        ITelegramBotClient botClient,
        Message message,
        CancellationToken cancellationToken = default)
    {
        if (message.Text == null || message.From == null)
        {
            return null;
        }

        var match = CommandPattern().Match(message.Text);
        if (!match.Success)
        {
            return null;
        }

        var commandName = match.Groups[1].Value.ToLowerInvariant();
        var args = match.Groups[2].Success
            ? match.Groups[2].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            : Array.Empty<string>();

        if (!_commands.TryGetValue(commandName, out var command))
        {
            return "❌ Unknown command. Use /help to see available commands.";
        }

        try
        {
            // TODO: Phase 2.3 - Link Telegram users to web app users
            // For now, all Telegram users have ReadOnly (0) permissions
            // Future: Add telegram_id column to users table or create telegram_users mapping table

            // TEMPORARY: Hardcode user 1312830442 as Owner for testing
            var permissionLevel = message.From.Id == 1312830442 ? 2 : 0;

            // Check permission
            if (permissionLevel < command.MinPermissionLevel)
            {
                _logger.LogWarning(
                    "User {UserId} ({Username}) attempted to use command /{Command} without sufficient permissions (has {UserLevel}, needs {RequiredLevel})",
                    message.From.Id, message.From.Username, commandName, permissionLevel, command.MinPermissionLevel);

                return $"❌ Insufficient permissions. This command requires {GetPermissionName(command.MinPermissionLevel)} level.";
            }

            // Check reply requirement
            if (command.RequiresReply && message.ReplyToMessage == null)
            {
                return $"❌ This command requires replying to a message.\n\nUsage: {command.Usage}";
            }

            // Execute command
            _logger.LogInformation(
                "Executing command /{Command} by user {UserId} ({Username}) with args: {Args}",
                commandName, message.From.Id, message.From.Username, string.Join(", ", args));

            var response = await command.ExecuteAsync(message, args, permissionLevel, cancellationToken);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing command /{Command} by user {UserId}", commandName, message.From?.Id);
            return $"❌ Error executing command: {ex.Message}";
        }
    }

    /// <summary>
    /// Get all available commands for a user's permission level
    /// </summary>
    public IEnumerable<IBotCommand> GetAvailableCommands(int permissionLevel)
    {
        return _commands.Values
            .Where(c => c.MinPermissionLevel <= permissionLevel)
            .OrderBy(c => c.Name);
    }

    private static string GetPermissionName(int level) => level switch
    {
        0 => "ReadOnly",
        1 => "Admin",
        2 => "Owner",
        _ => "Unknown"
    };
}
