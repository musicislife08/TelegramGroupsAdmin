using Telegram.Bot;
using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Services.BotCommands;

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
    /// Minimum permission level required (0=ReadOnly, 1=Admin, 2=Owner)
    /// </summary>
    int MinPermissionLevel { get; }

    /// <summary>
    /// Whether this command requires replying to a message
    /// </summary>
    bool RequiresReply { get; }

    /// <summary>
    /// Whether to delete the command message after execution (for moderation commands)
    /// </summary>
    bool DeleteCommandMessage { get; }

    /// <summary>
    /// Execute the command
    /// </summary>
    /// <param name="botClient">Telegram bot client for API calls</param>
    /// <param name="message">Telegram message containing the command</param>
    /// <param name="args">Command arguments (parsed after command name)</param>
    /// <param name="userPermissionLevel">Permission level of user who issued command</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Response message to send to user</returns>
    Task<string> ExecuteAsync(
        ITelegramBotClient botClient,
        Message message,
        string[] args,
        int userPermissionLevel,
        CancellationToken cancellationToken = default);
}
