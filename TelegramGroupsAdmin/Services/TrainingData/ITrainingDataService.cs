using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Services.TrainingData;

/// <summary>Training Data page operations. Every change is a verdict event; the latest wins.</summary>
public interface ITrainingDataService
{
    Task<IReadOnlyList<DetectionResultRecord>> GetSamplesAsync(CancellationToken cancellationToken = default);
    Task<TrainingDataStats> GetStatsAsync(CancellationToken cancellationToken = default);
    Task AddSampleAsync(string messageText, bool isSpam, Actor actor, string? translatedText, string? detectedLanguage,
        CancellationToken cancellationToken = default);
    Task ReplaceSampleAsync(int oldMessageId, long oldChatId, string messageText, bool isSpam, Actor actor,
        CancellationToken cancellationToken = default);
    Task ExcludeAsync(int messageId, long chatId, Actor actor, CancellationToken cancellationToken = default);

    /// <summary>
    /// Excludes multiple messages from training in one action (e.g. bulk duplicate removal).
    /// Records a TrainingExclude decision for each message, then retrains exactly once — never
    /// once per message. Retrains not at all when <paramref name="messages"/> is empty.
    /// </summary>
    Task ExcludeManyAsync(IReadOnlyCollection<(int MessageId, long ChatId)> messages, Actor actor,
        CancellationToken cancellationToken = default);
}
