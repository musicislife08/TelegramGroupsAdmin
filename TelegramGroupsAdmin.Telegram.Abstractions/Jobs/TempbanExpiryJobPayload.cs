namespace TelegramGroupsAdmin.Telegram.Abstractions.Jobs;

/// <summary>
/// Payload for TempbanExpiryJob - scheduled to run when a tempban expires
/// Calls UnbanChatMember() to completely remove user from "Removed users" list
/// Allows user to use invite links to rejoin chats
/// </summary>
public record TempbanExpiryJobPayload(
    long UserId,
    string Reason,
    DateTimeOffset ExpiresAt);
