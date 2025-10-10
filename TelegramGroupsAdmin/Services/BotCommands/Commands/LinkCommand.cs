using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Repositories;

namespace TelegramGroupsAdmin.Services.BotCommands.Commands;

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

    public LinkCommand(
        ILogger<LinkCommand> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public async Task<string> ExecuteAsync(
        ITelegramBotClient botClient,
        Message message,
        string[] args,
        int userPermissionLevel,
        CancellationToken cancellationToken = default)
    {
        // Validate token argument
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            return "❌ Please provide a link token: `/link <token>`\n\n" +
                   "Generate a token at: Profile → Linked Telegram Accounts";
        }

        var token = args[0].Trim();
        var telegramUser = message.From!;

        using var scope = _serviceProvider.CreateScope();
        var mappingRepository = scope.ServiceProvider.GetRequiredService<ITelegramUserMappingRepository>();
        var tokenRepository = scope.ServiceProvider.GetRequiredService<ITelegramLinkTokenRepository>();

        // Check if Telegram account is already linked
        var existingMapping = await mappingRepository.GetByTelegramIdAsync(telegramUser.Id);
        if (existingMapping != null)
        {
            return $"❌ Your Telegram account is already linked to a web app user.\n\n" +
                   $"To link a different account, first unlink from the web app.";
        }

        // Validate token
        var tokenRecord = await tokenRepository.GetByTokenAsync(token);
        if (tokenRecord == null)
        {
            return "❌ Invalid token. Please generate a new token from the web app.";
        }

        // Check if token is expired
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (tokenRecord.ExpiresAt < now)
        {
            return "❌ Token expired. Please generate a new token from the web app.";
        }

        // Check if token was already used
        if (tokenRecord.UsedAt != null)
        {
            return "❌ Token already used. Please generate a new token from the web app.";
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

        await mappingRepository.InsertAsync(mapping);

        // Mark token as used
        await tokenRepository.MarkAsUsedAsync(token, telegramUser.Id);

        _logger.LogInformation(
            "Telegram user {TelegramId} (@{Username}) successfully linked to web user {UserId}",
            telegramUser.Id,
            telegramUser.Username ?? "none",
            tokenRecord.UserId);

        return $"✅ Successfully linked your Telegram account!\n\n" +
               $"You can now use bot commands with your web app permissions.";
    }
}
