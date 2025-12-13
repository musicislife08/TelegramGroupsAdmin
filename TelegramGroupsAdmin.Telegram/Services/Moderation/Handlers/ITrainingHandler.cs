using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Handlers;

/// <summary>
/// Handler for spam detection training data.
/// Called by orchestrator for spam cases only.
/// </summary>
public interface ITrainingHandler
{
    /// <summary>
    /// Mark a message as spam training sample.
    /// </summary>
    Task CreateSpamSampleAsync(
        long messageId,
        Actor executor,
        CancellationToken ct = default);
}
