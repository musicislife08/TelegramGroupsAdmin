using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.BotCommands;

/// <summary>
/// Base interface for all bot commands
/// </summary>
public interface IBotCommand
{
    /// <summary>
    /// Command name (without leading slash, e.g., "spam", "ban")
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Command description for help text
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Usage example (e.g., "/spam <reply to message>")
    /// </summary>
    string Usage { get; }

    /// <summary>
    /// Minimum permission tier required to run this command.
    /// </summary>
    PermissionLevel MinPermissionLevel { get; }

    /// <summary>
    /// Whether this command requires replying to a message
    /// </summary>
    bool RequiresReply { get; }

    /// <summary>
    /// Whether to delete the command message after execution (for moderation commands)
    /// </summary>
    bool DeleteCommandMessage { get; }

    /// <summary>
    /// Auto-delete the bot's response after this many seconds (null = don't delete)
    /// Default value - commands can override in CommandResult
    /// </summary>
    int? DeleteResponseAfterSeconds { get; }

    /// <summary>
    /// Execute the command
    /// </summary>
    /// <param name="message">Telegram message containing the command</param>
    /// <param name="args">Command arguments (parsed after command name)</param>
    /// <param name="userPermission">Effective permission tier of the user who issued the command</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>CommandResult with response message and optional dynamic deletion time</returns>
    Task<CommandResult> ExecuteAsync(
        Message message,
        string[] args,
        PermissionLevel userPermission,
        CancellationToken cancellationToken = default);
}
