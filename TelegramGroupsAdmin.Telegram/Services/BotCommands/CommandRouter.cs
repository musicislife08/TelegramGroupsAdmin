using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Metrics;

namespace TelegramGroupsAdmin.Telegram.Services.BotCommands;

/// <summary>
/// Result of executing a bot command
/// </summary>
public record CommandResult(
    TelegramMessage Message,
    bool DeleteCommandMessage,
    int? DeleteResponseAfterSeconds = null);

/// <summary>
/// Routes bot commands to appropriate handlers with permission checking
/// </summary>
public partial class CommandRouter
{
    private readonly ILogger<CommandRouter> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly PipelineMetrics _pipelineMetrics;

    [GeneratedRegex(@"^/(\w+)(?:@\w+)?(?:\s+(.*))?$", RegexOptions.Compiled)]
    private static partial Regex CommandPattern();

    public CommandRouter(
        ILogger<CommandRouter> logger,
        IServiceProvider serviceProvider,
        PipelineMetrics pipelineMetrics)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _pipelineMetrics = pipelineMetrics;
    }

    /// <summary>
    /// Check if message contains a bot command
    /// </summary>
    public bool IsCommand(Message message)
    {
        if (message.Text == null) return false;

        var match = CommandPattern().Match(message.Text);
        return match.Success && CommandNames.All.Contains(match.Groups[1].Value);
    }

    /// <summary>
    /// Route and execute bot command
    /// </summary>
    /// <param name="message">Message containing the command.</param>
    /// <param name="sender">Identity of <c>message.From</c>: observed by the group pipeline, resolved by id for DMs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<CommandResult?> RouteCommandAsync(
        Message message,
        UserIdentity sender,
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
            : [];

        if (!CommandNames.All.Contains(commandName))
        {
            return new CommandResult(TelegramMessage.Plain("❌ Unknown command. Use /help to see available commands."), false);
        }

        try
        {
            // Create a scope and resolve command by key (keyed services pattern)
            using var scope = _serviceProvider.CreateScope();
            var command = scope.ServiceProvider.GetRequiredKeyedService<IBotCommand>(commandName);

            // Resolve the user's effective tier in this chat (web tier ⊕ chat-admin status).
            var permissionLevel = await scope.ServiceProvider.GetRequiredService<ITelegramPermissionService>()
                .GetEffectiveLevelAsync(message.Chat.Id, sender.Id, cancellationToken);

            // Gate: public commands are PermissionLevel.Member (the floor) so they never fail this.
            if (permissionLevel < command.MinPermissionLevel)
            {
                _logger.LogWarning(
                    "User {User} attempted /{Command} without sufficient permission (has {UserLevel}, needs {RequiredLevel})",
                    sender.ToLogDebug(),
                    commandName, permissionLevel, command.MinPermissionLevel);

                // Every gated command requires Admin (moderation); public commands are Member (the floor)
                // and can never reach this branch, so the only denial is "needs admin".
                return new CommandResult(
                    TelegramMessage.Plain("❌ This command is only available to group administrators."),
                    true); // Auto-delete permission denied messages
            }

            // Check reply requirement
            if (command.RequiresReply && message.ReplyToMessage == null)
            {
                return new CommandResult(TelegramMessage.Plain($"❌ This command requires replying to a message.\n\nUsage: {command.Usage}"), false);
            }

            // Execute command
            _logger.LogInformation(
                "Executing command /{Command} by user {User} with args: {Args}",
                commandName, sender.ToLogDebug(),
                string.Join(", ", args));

            var result = await command.ExecuteAsync(message, args, permissionLevel, sender, cancellationToken);
            _pipelineMetrics.RecordCommandHandled(commandName);

            // Commands can now return dynamic CommandResult or use defaults from interface properties
            return result;
        }
        catch (Exception ex)
        {
            // The reply is posted in the chat, so it never carries exception text; the log has it.
            _logger.LogError(ex, "Error executing command /{Command} by {User}", commandName, sender.ToLogDebug());
            return new CommandResult(TelegramMessage.Plain("❌ Something went wrong running that command."), false);
        }
    }

    /// <summary>
    /// Get all available commands for a user's permission level
    /// </summary>
    public IEnumerable<IBotCommand> GetAvailableCommands(PermissionLevel permissionLevel)
    {
        using var scope = _serviceProvider.CreateScope();

        var commands = new List<IBotCommand>();
        foreach (var commandName in CommandNames.All)
        {
            var command = scope.ServiceProvider.GetRequiredKeyedService<IBotCommand>(commandName);
            if (command.MinPermissionLevel <= permissionLevel)
            {
                commands.Add(command);
            }
        }

        return commands.OrderBy(c => c.Name);
    }
}
