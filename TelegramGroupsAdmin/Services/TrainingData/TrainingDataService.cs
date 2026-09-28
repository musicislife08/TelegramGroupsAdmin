using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;

namespace TelegramGroupsAdmin.Services.TrainingData;

public sealed class TrainingDataService(
    IDetectionResultsRepository detectionResultsRepository,
    IJobTriggerService jobTriggerService,
    ILogger<TrainingDataService> logger) : ITrainingDataService
{
    public async Task<IReadOnlyList<DetectionResultRecord>> GetSamplesAsync(CancellationToken cancellationToken = default)
        => await detectionResultsRepository.GetAllTrainingDataAsync(cancellationToken);

    public Task<TrainingDataStats> GetStatsAsync(CancellationToken cancellationToken = default)
        => detectionResultsRepository.GetTrainingDataStatsAsync(cancellationToken);

    public async Task AddSampleAsync(string messageText, bool isSpam, Actor actor, string? translatedText,
        string? detectedLanguage, CancellationToken cancellationToken = default)
    {
        await detectionResultsRepository.AddManualTrainingSampleAsync(
            messageText, isSpam, actor, translatedText, detectedLanguage, cancellationToken);
        await RetrainAsync(cancellationToken);
    }

    public async Task ReplaceSampleAsync(int oldMessageId, long oldChatId, string messageText, bool isSpam, Actor actor,
        CancellationToken cancellationToken = default)
    {
        await RecordExclusionAsync(oldMessageId, oldChatId, actor, cancellationToken);
        await detectionResultsRepository.AddManualTrainingSampleAsync(messageText, isSpam, actor, cancellationToken: cancellationToken);
        await RetrainAsync(cancellationToken);
    }

    public async Task ExcludeAsync(int messageId, long chatId, Actor actor, CancellationToken cancellationToken = default)
    {
        await RecordExclusionAsync(messageId, chatId, actor, cancellationToken);
        await RetrainAsync(cancellationToken);
    }

    private async Task RecordExclusionAsync(int messageId, long chatId, Actor actor, CancellationToken cancellationToken)
    {
        var current = await detectionResultsRepository.GetCurrentVerdictAsync(messageId, chatId, cancellationToken)
            ?? throw new InvalidOperationException($"Message {messageId} in chat {chatId} not found");

        await detectionResultsRepository.RecordDecisionAsync(messageId, chatId, VerdictSource.TrainingExclude, actor,
            "Removed from training data", isSpam: current.IsSpam, cancellationToken: cancellationToken);

        logger.LogInformation("Excluded message {MessageId} in chat {ChatId} from training (was {Classification})",
            messageId, chatId, current.Classification);
    }

    private Task RetrainAsync(CancellationToken cancellationToken) =>
        jobTriggerService.TriggerNowAsync(BackgroundJobNames.ClassifierRetraining, payload: new { }, cancellationToken: cancellationToken);
}
