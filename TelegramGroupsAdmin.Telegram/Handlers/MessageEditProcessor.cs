using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Handlers;

/// <summary>
/// Handles message edit processing: edit history, translation, message updates, spam re-scanning.
/// Encapsulates the entire edit workflow from detection to spam analysis.
/// </summary>
public class MessageEditProcessor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MessageEditProcessor> _logger;

    public MessageEditProcessor(
        IServiceProvider serviceProvider,
        ILogger<MessageEditProcessor> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Process edited message: save edit history, translate if needed, update message, trigger spam re-scan.
    /// Returns the edit record for event raising (or null if no actual edit occurred).
    /// </summary>
    public async Task<MessageEditRecord?> ProcessEditAsync(
        ITelegramBotClient botClient,
        Message editedMessage,
        IServiceScope scope,
        CancellationToken cancellationToken = default)
    {
        var repository = scope.ServiceProvider.GetRequiredService<IMessageHistoryRepository>();

        // Get the old message from the database
        var oldMessage = await repository.GetMessageAsync(editedMessage.MessageId, cancellationToken);
        if (oldMessage == null)
        {
            _logger.LogWarning(
                "Received edit for unknown message {MessageId}",
                editedMessage.MessageId);
            return null;
        }

        var oldText = oldMessage.MessageText;
        var newText = editedMessage.Text ?? editedMessage.Caption;

        // Skip if text hasn't actually changed
        if (oldText == newText)
        {
            _logger.LogDebug(
                "Edit event for message {MessageId} but text unchanged, skipping",
                editedMessage.MessageId);
            return null;
        }

        var editDate = editedMessage.EditDate.HasValue
            ? new DateTimeOffset(editedMessage.EditDate.Value, TimeSpan.Zero)
            : DateTimeOffset.UtcNow;

        // Extract URLs and calculate content hashes
        var oldUrls = UrlUtilities.ExtractUrls(oldText);
        var newUrls = UrlUtilities.ExtractUrls(newText);

        var oldContentHash = HashUtilities.ComputeContentHash(oldText ?? "", oldUrls != null ? JsonSerializer.Serialize(oldUrls) : "");
        var newContentHash = HashUtilities.ComputeContentHash(newText ?? "", newUrls != null ? JsonSerializer.Serialize(newUrls) : "");

        // Create edit record
        var editRecord = new MessageEditRecord(
            Id: 0, // Will be set by INSERT
            MessageId: editedMessage.MessageId,
            EditDate: editDate,
            OldText: oldText,
            NewText: newText,
            OldContentHash: oldContentHash,
            NewContentHash: newContentHash
        );

        await repository.InsertMessageEditAsync(editRecord, cancellationToken);

        // Phase 4.20: Translate edited message if non-English (before spam detection)
        await TranslateEditIfNeededAsync(repository, scope, editedMessage, newText, cancellationToken);

        // Update the message in the messages table with new text and edit date
        var updatedMessage = oldMessage with
        {
            MessageText = newText,
            EditDate = editDate,
            Urls = newUrls != null ? JsonSerializer.Serialize(newUrls) : null,
            ContentHash = newContentHash
        };
        await repository.UpdateMessageAsync(updatedMessage, cancellationToken);

        _logger.LogInformation(
            "Recorded edit for message {MessageId} in chat {ChatId}",
            editedMessage.MessageId,
            editedMessage.Chat.Id);

        // Schedule spam re-scan in background
        await ScheduleSpamReScanAsync(botClient, editedMessage, newText);

        return editRecord;
    }

    /// <summary>
    /// Translate edited message if it's non-English and meets config requirements.
    /// </summary>
    private async Task TranslateEditIfNeededAsync(
        IMessageHistoryRepository repository,
        IServiceScope scope,
        Message editedMessage,
        string? newText,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(newText))
            return;

        // Re-fetch edit to get generated ID
        var editsForMessage = await repository.GetEditsForMessageAsync(editedMessage.MessageId, cancellationToken);
        var savedEdit = editsForMessage.OrderByDescending(e => e.EditDate).FirstOrDefault();

        if (savedEdit == null)
            return;

        var spamConfigRepo = scope.ServiceProvider.GetRequiredService<TelegramGroupsAdmin.ContentDetection.Repositories.ISpamDetectionConfigRepository>();
        var spamConfig = await spamConfigRepo.GetGlobalConfigAsync(cancellationToken);

        // Check if translation is enabled and message meets minimum length
        if (!spamConfig.Translation.Enabled || newText.Length < spamConfig.Translation.MinMessageLength)
            return;

        // Use TranslationHandler's static method for Latin script detection
        var latinRatio = TranslationHandler.CalculateLatinScriptRatio(newText);
        if (latinRatio >= spamConfig.Translation.LatinScriptThreshold)
            return;

        var translationService = scope.ServiceProvider.GetRequiredService<TelegramGroupsAdmin.ContentDetection.Services.IOpenAITranslationService>();
        var translationResult = await translationService.TranslateToEnglishAsync(newText, cancellationToken);

        if (translationResult != null && translationResult.WasTranslated)
        {
            var translation = new MessageTranslation(
                Id: 0, // Will be set by INSERT
                MessageId: null, // Exclusive arc: translation belongs to EDIT, not message
                EditId: savedEdit.Id,
                TranslatedText: translationResult.TranslatedText,
                DetectedLanguage: translationResult.DetectedLanguage,
                Confidence: null, // OpenAI doesn't return confidence for translation
                TranslatedAt: DateTimeOffset.UtcNow
            );

            await repository.InsertTranslationAsync(translation, cancellationToken);

            _logger.LogInformation(
                "Translated edit #{EditId} for message {MessageId} from {Language} to English",
                savedEdit.Id,
                editedMessage.MessageId,
                translationResult.DetectedLanguage);
        }
    }

    /// <summary>
    /// Schedule spam re-scan for edited message in background task.
    /// Detects "post innocent, edit to spam" tactic.
    /// </summary>
    private Task ScheduleSpamReScanAsync(
        ITelegramBotClient botClient,
        Message editedMessage,
        string? newText)
    {
        if (string.IsNullOrWhiteSpace(newText))
            return Task.CompletedTask;

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var detectionResultsRepo = scope.ServiceProvider.GetRequiredService<IDetectionResultsRepository>();

                // Get the latest edit_version for this message
                var existingResults = await detectionResultsRepo.GetByMessageIdAsync(editedMessage.MessageId, CancellationToken.None);
                var maxEditVersion = existingResults.Any()
                    ? existingResults.Max(r => r.EditVersion)
                    : 0;

                var contentOrchestrator = scope.ServiceProvider.GetRequiredService<ContentDetectionOrchestrator>();
                await contentOrchestrator.RunDetectionAsync(botClient, editedMessage, newText, photoLocalPath: null, editVersion: maxEditVersion + 1, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to re-scan edited message {MessageId} for spam", editedMessage.MessageId);
            }
        }, CancellationToken.None);

        return Task.CompletedTask;
    }
}
