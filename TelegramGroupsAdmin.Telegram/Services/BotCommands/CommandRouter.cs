using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services.BotCommands;

/// <summary>
/// Result of executing a bot command
/// </summary>
public record CommandResult(string? Response, bool DeleteCommandMessage);

/// <summary>
/// Routes bot commands to appropriate handlers with permission checking
/// </summary>
public partial class CommandRouter
{
    private readonly ILogger<CommandRouter> _logger;
    private readonly Dictionary<string, Type> _commandTypes;
    private readonly IServiceProvider _serviceProvider;

    [GeneratedRegex(@"^/(\w+)(?:@\w+)?(?:\s+(.*))?$", RegexOptions.Compiled)]
    private static partial Regex CommandPattern();

    public CommandRouter(
        ILogger<CommandRouter> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;

        // Resolve commands from a temporary scope to discover their types and names
        // (we can't inject IEnumerable<IBotCommand> because commands are Scoped and router is Singleton)
        using var scope = serviceProvider.CreateScope();
        var commands = scope.ServiceProvider.GetServices<IBotCommand>();
        _commandTypes = commands.ToDictionary(
            c => c.Name.ToLowerInvariant(),
            c => c.GetType());
    }

    /// <summary>
    /// Check if message contains a bot command
    /// </summary>
    public bool IsCommand(Message message)
    {
        if (message.Text == null) return false;

        var match = CommandPattern().Match(message.Text);
        return match.Success && _commandTypes.ContainsKey(match.Groups[1].Value.ToLowerInvariant());
    }

    /// <summary>
    /// Route and execute bot command
    /// </summary>
    public async Task<CommandResult?> RouteCommandAsync(
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

        if (!_commandTypes.TryGetValue(commandName, out var commandType))
        {
            return new CommandResult("❌ Unknown command. Use /help to see available commands.", false);
        }

        try
        {
            // Create a scope to resolve the command (commands are Scoped to allow injecting Scoped services)
            using var scope = _serviceProvider.CreateScope();
            var command = (IBotCommand)scope.ServiceProvider.GetRequiredService(commandType);

            // Special case: /link command is always accessible (doesn't require linking)
            var permissionLevel = commandName == "link"
                ? 0
                : await GetPermissionLevelAsync(botClient, message.Chat.Id, message.From.Id);

            // Check permission
            if (permissionLevel < command.MinPermissionLevel)
            {
                _logger.LogWarning(
                    "User {UserId} (@{Username}) attempted to use command /{Command} without sufficient permissions (has {UserLevel}, needs {RequiredLevel})",
                    message.From.Id, message.From.Username ?? "none", commandName, permissionLevel, command.MinPermissionLevel);

                var permissionMessage = permissionLevel == -1
                    ? $"❌ You don't have permission to use this command.\n\n" +
                      $"• Telegram group admins can use admin commands automatically\n" +
                      $"• Or link your web app account: /link <token>\n" +
                      $"  (Generate token at: Profile → Linked Telegram Accounts)"
                    : $"❌ Insufficient permissions. This command requires {GetPermissionName(command.MinPermissionLevel)} level.";

                return new CommandResult(permissionMessage, false);
            }

            // Check reply requirement
            if (command.RequiresReply && message.ReplyToMessage == null)
            {
                return new CommandResult($"❌ This command requires replying to a message.\n\nUsage: {command.Usage}", false);
            }

            // Execute command
            _logger.LogInformation(
                "Executing command /{Command} by user {UserId} ({Username}) with args: {Args}",
                commandName, message.From.Id, message.From.Username, string.Join(", ", args));

            var response = await command.ExecuteAsync(botClient, message, args, permissionLevel, cancellationToken);

            return new CommandResult(response, command.DeleteCommandMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing command /{Command} by user {UserId}", commandName, message.From?.Id);
            return new CommandResult($"❌ Error executing command: {ex.Message}", false);
        }
    }

    /// <summary>
    /// Get all available commands for a user's permission level
    /// </summary>
    public IEnumerable<IBotCommand> GetAvailableCommands(int permissionLevel)
    {
        // Create a scope to resolve commands (they're Scoped)
        using var scope = _serviceProvider.CreateScope();

        var commands = new List<IBotCommand>();
        foreach (var commandType in _commandTypes.Values)
        {
            var command = (IBotCommand)scope.ServiceProvider.GetRequiredService(commandType);
            if (command.MinPermissionLevel <= permissionLevel)
            {
                commands.Add(command);
            }
        }

        return commands.OrderBy(c => c.Name);
    }

    /// <summary>
    /// Get permission level for a Telegram user
    /// Priority order:
    /// 1. Linked web app user → their permission level (0-2, global across all chats)
    /// 2. Telegram group creator → Owner (2, per-chat only)
    /// 3. Telegram group admin → Admin (1, per-chat only)
    /// 4. Not linked and not admin → -1 (no permission)
    /// </summary>
    private async Task<int> GetPermissionLevelAsync(ITelegramBotClient botClient, long chatId, long telegramId)
    {
        using var scope = _serviceProvider.CreateScope();

        // Check web app linking FIRST (global permissions, works in all chats)
        var mappingRepository = scope.ServiceProvider.GetRequiredService<ITelegramUserMappingRepository>();
        var userId = await mappingRepository.GetUserIdByTelegramIdAsync(telegramId);

        if (userId != null)
        {
            var userRepository = scope.ServiceProvider.GetRequiredService<UserRepository>();
            var user = await userRepository.GetByIdAsync(userId);

            if (user != null)
            {
                _logger.LogDebug("User {TelegramId} is linked to web app user with {PermissionLevel} permissions (global)",
                    telegramId, user.PermissionLevel);
                return (int)user.PermissionLevel; // 0=ReadOnly, 1=Admin, 2=Owner (global)
            }
        }

        // Check Telegram admin permissions (cached, per-chat only)
        var chatAdminsRepository = scope.ServiceProvider.GetRequiredService<IChatAdminsRepository>();
        var adminPermissionLevel = await chatAdminsRepository.GetPermissionLevelAsync(chatId, telegramId);

        if (adminPermissionLevel > -1)
        {
            var roleName = adminPermissionLevel == 2 ? "creator" : "admin";
            _logger.LogDebug("User {TelegramId} is Telegram {Role} in chat {ChatId}, granting {Level} permissions (per-chat)",
                telegramId, roleName, chatId, adminPermissionLevel);
            return adminPermissionLevel; // 1=Admin or 2=Creator (per-chat)
        }

        // Not linked and not admin
        _logger.LogDebug("User {TelegramId} has no permissions in chat {ChatId}", telegramId, chatId);
        return -1;
    }

    private static string GetPermissionName(int level) => level switch
    {
        0 => "ReadOnly",
        1 => "Admin",
        2 => "Owner",
        _ => "Unknown"
    };
}
