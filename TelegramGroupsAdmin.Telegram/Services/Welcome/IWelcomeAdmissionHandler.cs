using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.Welcome;

/// <summary>
/// Centralized admission gate for the welcome flow.
/// Checks all gates (profile scan, welcome response) and restores permissions
/// only when all gates are clear. Callers do their own domain work first,
/// then delegate the unmute decision here.
/// </summary>
public interface IWelcomeAdmissionHandler
{
    /// <summary>
    /// Check all admission gates and restore permissions if all clear.
    /// </summary>
    /// <param name="user">User to potentially admit.</param>
    /// <param name="chat">Chat to admit user into.</param>
    /// <param name="executor">Actor performing the admission (e.g. WelcomeFlow, Admin).</param>
    /// <param name="reason">Audit reason for permission restore.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Admitted if user was unmuted, StillWaiting if any gate is pending.</returns>
    Task<AdmissionResult> TryAdmitUserAsync(
        UserIdentity user, ChatIdentity chat, Actor executor, string reason, CancellationToken ct);
}
