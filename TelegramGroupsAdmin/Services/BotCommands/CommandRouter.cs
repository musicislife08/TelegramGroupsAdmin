using System.Text.RegularExpressions;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Repositories;

namespace TelegramGroupsAdmin.Services.BotCommands;

/// <summary>
/// Routes bot commands to appropriate handlers with permission checking
/// </summary>
public partial class CommandRouter
{
    private readonly ILogger<CommandRouter> _logger;
    private readonly Dictionary<string, IBotCommand> _commands;
    private readonly ITelegramUserMappingRepository _mappingRepository;
    private readonly IServiceProvider _serviceProvider;

    [GeneratedRegex(@"^/(\w+)(?:@\w+)?(?:\s+(.*))?$", RegexOptions.Compiled)]
    private static partial Regex CommandPattern();

    public CommandRouter(
        ILogger<CommandRouter> logger,
        IEnumerable<IBotCommand> commands,
        ITelegramUserMappingRepository mappingRepository,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _commands = commands.ToDictionary(c => c.Name.ToLowerInvariant(), c => c);
        _mappingRepository = mappingRepository;
        _serviceProvider = serviceProvider;
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
            // Special case: /link command is always accessible (doesn't require linking)
            var permissionLevel = commandName == "link" ? 0 : await GetPermissionLevelAsync(message.From.Id);

            // Check permission
            if (permissionLevel < command.MinPermissionLevel)
            {
                _logger.LogWarning(
                    "User {UserId} (@{Username}) attempted to use command /{Command} without sufficient permissions (has {UserLevel}, needs {RequiredLevel})",
                    message.From.Id, message.From.Username ?? "none", commandName, permissionLevel, command.MinPermissionLevel);

                return permissionLevel == -1
                    ? $"❌ Please link your Telegram account first. Use /link <token>\n\nGenerate a token at the web app: Profile → Linked Telegram Accounts"
                    : $"❌ Insufficient permissions. This command requires {GetPermissionName(command.MinPermissionLevel)} level.";
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

    /// <summary>
    /// Get permission level for a Telegram user
    /// Returns -1 if not linked, otherwise returns the user's permission level from web app
    /// </summary>
    private async Task<int> GetPermissionLevelAsync(long telegramId)
    {
        // Special case: /link command should always be accessible (checked in LinkCommand itself)
        // Get the user_id from telegram_user_mappings
        var userId = await _mappingRepository.GetUserIdByTelegramIdAsync(telegramId);
        if (userId == null)
        {
            return -1; // Not linked
        }

        // Get permission level from users table
        using var scope = _serviceProvider.CreateScope();
        var userRepository = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var user = await userRepository.GetByIdAsync(userId);

        return user != null ? (int)user.PermissionLevel : -1;
    }

    private static string GetPermissionName(int level) => level switch
    {
        0 => "ReadOnly",
        1 => "Admin",
        2 => "Owner",
        _ => "Unknown"
    };
}
