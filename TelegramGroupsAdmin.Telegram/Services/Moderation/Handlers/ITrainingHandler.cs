using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Handlers;

/// <summary>
/// Handler for spam detection training data.
/// Records spam and ham decisions as verdict events.
/// </summary>
public interface ITrainingHandler
{
    /// <summary>
    /// Records a spam decision (ExplicitSpam) and triggers retraining. Every executor records
    /// a decision, System included; auto-bans are explicit by design.
    /// </summary>
    Task CreateSpamSampleAsync(int messageId, ChatIdentity chat, Actor executor, VerdictSource source, string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a ham decision: WebMarkHam (ExplicitHam) or ReviewDismiss (ImplicitHam), and
    /// triggers retraining. The newer event supersedes older spam events everywhere.
    /// </summary>
    Task CreateHamSampleAsync(int messageId, ChatIdentity chat, Actor executor, VerdictSource source, string reason,
        CancellationToken cancellationToken = default);
}
