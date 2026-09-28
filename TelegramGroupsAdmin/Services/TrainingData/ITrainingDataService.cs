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
}
