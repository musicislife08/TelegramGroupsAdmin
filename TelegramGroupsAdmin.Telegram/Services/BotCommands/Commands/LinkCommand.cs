using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;

public class LinkCommand : IBotCommand
{
    private readonly ILogger<LinkCommand> _logger;
    private readonly IServiceProvider _serviceProvider;

    public string Name => "link";
    public string Description => "Link your Telegram account to web app";
    public string Usage => "/link <token>";
    public int MinPermissionLevel => 0; // Anyone can link
    public bool RequiresReply => false;
    public bool DeleteCommandMessage => true; // Delete for security (contains token)
    public int? DeleteResponseAfterSeconds => null;

    public LinkCommand(
        ILogger<LinkCommand> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public async Task<CommandResult> ExecuteAsync(
        ITelegramBotClient botClient,
        Message message,
        string[] args,
        int userPermissionLevel,
        CancellationToken cancellationToken = default)
    {
        // Validate token argument
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            return new CommandResult(
                "❌ Please provide a link token: `/link <token>`\n\n" +
                "Generate a token at: Profile → Linked Telegram Accounts",
                DeleteCommandMessage,
                DeleteResponseAfterSeconds);
        }

        var token = args[0].Trim();
        var telegramUser = message.From!;

        using var scope = _serviceProvider.CreateScope();
        var mappingRepository = scope.ServiceProvider.GetRequiredService<ITelegramUserMappingRepository>();
        var tokenRepository = scope.ServiceProvider.GetRequiredService<ITelegramLinkTokenRepository>();

        // Check if Telegram account is already linked
        var existingMapping = await mappingRepository.GetByTelegramIdAsync(telegramUser.Id, cancellationToken);
        if (existingMapping != null)
        {
            return new CommandResult(
                $"❌ Your Telegram account is already linked to a web app user.\n\n" +
                $"To link a different account, first unlink from the web app.",
                DeleteCommandMessage,
                DeleteResponseAfterSeconds);
        }

        // Validate token
        var tokenRecord = await tokenRepository.GetByTokenAsync(token, cancellationToken);
        if (tokenRecord == null)
        {
            return new CommandResult(
                "❌ Invalid token. Please generate a new token from the web app.",
                DeleteCommandMessage,
                DeleteResponseAfterSeconds);
        }

        // Check if token is expired
        var now = DateTimeOffset.UtcNow;
        if (tokenRecord.ExpiresAt < now)
        {
            return new CommandResult(
                "❌ Token expired. Please generate a new token from the web app.",
                DeleteCommandMessage,
                DeleteResponseAfterSeconds);
        }

        // Check if token was already used
        if (tokenRecord.UsedAt != null)
        {
            return new CommandResult(
                "❌ Token already used. Please generate a new token from the web app.",
                DeleteCommandMessage,
                DeleteResponseAfterSeconds);
        }

        // Create mapping
        var mapping = new Models.TelegramUserMappingRecord(
            Id: 0, // Will be assigned by database
            TelegramId: telegramUser.Id,
            TelegramUsername: telegramUser.Username,
            UserId: tokenRecord.UserId,
            LinkedAt: now,
            IsActive: true
        );

        await mappingRepository.InsertAsync(mapping, cancellationToken);

        // Mark token as used
        await tokenRepository.MarkAsUsedAsync(token, telegramUser.Id, cancellationToken);

        _logger.LogInformation(
            "Telegram user {TelegramId} (@{Username}) successfully linked to web user {UserId}",
            telegramUser.Id,
            telegramUser.Username ?? "none",
            tokenRecord.UserId);

        return new CommandResult(
            $"✅ Successfully linked your Telegram account!\n\n" +
            $"You can now use bot commands with your web app permissions.",
            DeleteCommandMessage,
            DeleteResponseAfterSeconds);
    }
}
