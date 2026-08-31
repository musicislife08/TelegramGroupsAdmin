using Microsoft.Extensions.DependencyInjection;
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
        using var scope = _serviceProvider.CreateScope();
        var available = CommandNames.All
            .Select(name => scope.ServiceProvider.GetRequiredKeyedService<IBotCommand>(name))
            .Where(c => c.Name != "start")                  // /start is deep-link/DM-only, not listed
            .Where(c => c.MinPermissionLevel <= userPermission)
            .OrderBy(c => c.Name)
            .ToList();

        var publicCommands = available.Where(c => c.MinPermissionLevel < PermissionLevel.Admin).ToList();
        var adminCommands = available.Where(c => c.MinPermissionLevel >= PermissionLevel.Admin).ToList();

        var builder = new TelegramMessageBuilder()
            .Text("🤖 ").Bold("TelegramGroupsAdmin Bot").LineBreak()
            .LineBreak();

        foreach (var c in publicCommands)
        {
            AppendCommandLine(builder, GetCommandEmoji(c.Name), c.Name, c.Description);
        }

        if (adminCommands.Count > 0 && userPermission >= PermissionLevel.Admin)
        {
            builder.LineBreak().Bold("Admin Commands:").LineBreak();
            foreach (var c in adminCommands)
            {
                AppendCommandLine(builder, GetCommandEmoji(c.Name), c.Name, c.Description);
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
        "link" => "🔑",
        "mystatus" => "👤",
        "mute" => "🔇",
        "delete" => "🗑️",
        "spam" => "🚫",
        "ban" => "⛔",
        "tempban" => "⏱️",
        "trust" => "✅",
        "unban" => "🔓",
        "warn" => "⚠️",
        _ => "🔹"
    };
}
