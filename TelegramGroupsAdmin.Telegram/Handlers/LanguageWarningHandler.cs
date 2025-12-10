using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.Telegram.Handlers;

/// <summary>
/// Handles language warnings for non-English messages from untrusted users.
/// Phase 4.21: Warns users who post non-English content, with configurable warning message.
/// </summary>
public class LanguageWarningHandler
{
    private readonly TelegramBotClientFactory _botFactory;
    private readonly ILogger<LanguageWarningHandler> _logger;

    public LanguageWarningHandler(
        TelegramBotClientFactory botFactory,
        ILogger<LanguageWarningHandler> logger)
    {
        _botFactory = botFactory;
        _logger = logger;
    }

    /// <summary>
    /// Handle language warning for non-English non-spam messages from untrusted users.
    /// Checks translation, config, user status, then issues warning via moderation system.
    /// </summary>
    public async Task HandleWarningAsync(
        Message message,
        IServiceScope scope,
        CancellationToken cancellationToken)
    {
        try
        {
            // Get message translation to check if it was non-English
            var translationService = scope.ServiceProvider.GetRequiredService<IMessageTranslationService>();
            var translation = await translationService.GetTranslationForMessageAsync(message.MessageId, cancellationToken);

            // Skip if no translation (message was in English)
            if (translation == null)
                return;

            // Get configuration
            var spamConfigRepo = scope.ServiceProvider.GetRequiredService<TelegramGroupsAdmin.ContentDetection.Repositories.IContentDetectionConfigRepository>();
            var spamConfig = await spamConfigRepo.GetGlobalConfigAsync(cancellationToken);

            // Check if language warnings are enabled
            if (!spamConfig.Translation.WarnNonEnglish)
                return;

            // Check if user is trusted or admin (skip warning for them)
            var userRepo = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
            var user = await userRepo.GetByIdAsync(message.From!.Id, cancellationToken);

            if (user?.IsTrusted == true)
                return;

            // Check if user is admin in this chat
            var operations = await _botFactory.GetOperationsAsync();
            var chatMember = await operations.GetChatMemberAsync(message.Chat.Id, message.From.Id, cancellationToken);
            if (chatMember.Status is ChatMemberStatus.Administrator or ChatMemberStatus.Creator)
                return;

            // Get warning system config for auto-ban threshold
            var configService = scope.ServiceProvider.GetRequiredService<TelegramGroupsAdmin.Configuration.Services.IConfigService>();
            var warningConfig = await configService.GetEffectiveAsync<WarningSystemConfig>(ConfigType.Moderation, message.Chat.Id)
                               ?? WarningSystemConfig.Default;

            // Get current warning count
            var userActionsRepo = scope.ServiceProvider.GetRequiredService<IUserActionsRepository>();
            var currentWarnings = await userActionsRepo.GetWarnCountAsync(message.From.Id, message.Chat.Id, cancellationToken);

            // Calculate warnings remaining
            var warningsRemaining = warningConfig.AutoBanThreshold - currentWarnings;
            if (warningsRemaining <= 0)
                warningsRemaining = 1; // Edge case: user already at threshold but not banned yet

            // Build warning message with variable substitution (in English)
            var chatName = message.Chat.Title ?? "this chat";
            var warningMessage = spamConfig.Translation.WarningMessage
                .Replace("{chat_name}", chatName)
                .Replace("{language}", translation.DetectedLanguage)
                .Replace("{warnings_remaining}", warningsRemaining.ToString());

            // Issue warning using moderation system
            var moderationService = scope.ServiceProvider.GetRequiredService<ModerationActionService>();
            await moderationService.WarnUserAsync(
                userId: message.From.Id,
                messageId: message.MessageId,
                executor: Actor.LanguageWarning,
                reason: $"Non-English message ({translation.DetectedLanguage})",
                chatId: message.Chat.Id,
                cancellationToken: cancellationToken);

            // Send warning to user via DM with chat fallback
            var messagingService = scope.ServiceProvider.GetRequiredService<IUserMessagingService>();
            await messagingService.SendToUserAsync(
                userId: message.From.Id,
                chatId: message.Chat.Id,
                messageText: warningMessage,
                replyToMessageId: message.MessageId,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Language warning issued to user {UserId} for {Language} message in chat {ChatId} ({Warnings}/{Threshold})",
                message.From.Id,
                translation.DetectedLanguage,
                message.Chat.Id,
                currentWarnings + 1,
                warningConfig.AutoBanThreshold);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle language warning for message {MessageId}", message.MessageId);
        }
    }
}
