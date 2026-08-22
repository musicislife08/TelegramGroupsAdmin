using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions;

/// <summary>
/// Domain handler for removing a user's leftover welcome/exam teaser message from chat.
///
/// The welcome flow reuses one message for its whole lifecycle: the "Verifying..." post is
/// edited into the welcome or exam teaser, and welcome_responses.welcome_message_id is the
/// only handle anyone has for deleting it. WelcomeTimeoutJob is the only unconditional
/// cleaner, and it is cancelled as soon as the user responds — so a user who responded and
/// was then banned during admin review leaves that message stranded in the chat.
///
/// Deliberately does NOT write welcome_responses.response: the final state
/// (Denied / Timeout / Left) belongs to the caller, which writes it after the moderation call.
/// </summary>
public interface IWelcomeCleanupHandler
{
    /// <summary>
    /// Delete the user's welcome message in one chat, or in every chat when
    /// <paramref name="chat"/> is null (global ban).
    /// </summary>
    /// <returns>Number of messages actually deleted.</returns>
    Task<int> DeleteStrandedWelcomeMessagesAsync(
        UserIdentity user,
        ChatIdentity? chat,
        Actor executor,
        CancellationToken cancellationToken = default);
}
