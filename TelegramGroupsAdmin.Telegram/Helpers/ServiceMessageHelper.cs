using Telegram.Bot.Types;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.Telegram.Helpers;

/// <summary>
/// Helper methods for identifying and classifying Telegram service messages.
/// Service messages are system-generated notifications (joins, leaves, photo changes, etc.)
/// </summary>
public static class ServiceMessageHelper
{
    /// <summary>
    /// Determines if a message is a service message and whether it should be deleted
    /// based on the provided configuration.
    /// </summary>
    /// <param name="message">The Telegram message to check</param>
    /// <param name="config">The service message deletion configuration</param>
    /// <param name="shouldDelete">Output: true if the message should be deleted based on config</param>
    /// <returns>True if the message is a service message, false otherwise</returns>
    public static bool IsServiceMessage(
        Message message,
        ServiceMessageDeletionConfig config,
        out bool shouldDelete)
    {
        switch (message)
        {
            case { NewChatMembers: not null }:
                shouldDelete = config.DeleteJoinMessages;
                return true;

            case { LeftChatMember: not null }:
                shouldDelete = config.DeleteLeaveMessages;
                return true;

            case { NewChatPhoto: not null }:
            case { DeleteChatPhoto: true }:
                shouldDelete = config.DeletePhotoChanges;
                return true;

            case { NewChatTitle: not null }:
                shouldDelete = config.DeleteTitleChanges;
                return true;

            case { PinnedMessage: not null }:
                shouldDelete = config.DeletePinNotifications;
                return true;

            case { GroupChatCreated: true }:
            case { SupergroupChatCreated: true }:
            case { ChannelChatCreated: true }:
                shouldDelete = config.DeleteChatCreationMessages;
                return true;

            default:
                shouldDelete = false;
                return false;
        }
    }

    /// <summary>
    /// Generate human-readable text for a service message (mirrors Telegram Desktop display).
    /// </summary>
    public static string? GetServiceMessageText(Message message)
    {
        return message switch
        {
            { NewChatMembers: not null } => FormatJoinMessage(message),
            { LeftChatMember: not null } => $"{TelegramDisplayName.Format(message.LeftChatMember)} left the group",
            { NewChatTitle: not null } => $"Group name changed to \"{message.NewChatTitle}\"",
            { NewChatPhoto: not null } => "Group photo updated",
            { DeleteChatPhoto: true } => "Group photo removed",
            { PinnedMessage: not null } => "Message pinned",
            _ => null
        };
    }

    private static string FormatJoinMessage(Message message)
    {
        var members = message.NewChatMembers!;
        if (members.Length == 1)
        {
            var user = members[0];
            // Check if user joined themselves or was added by someone else
            if (message.From?.Id == user.Id)
                return $"{TelegramDisplayName.Format(user)} joined the group";
            else
                return $"{TelegramDisplayName.Format(message.From)} added {TelegramDisplayName.Format(user)}";
        }

        var names = string.Join(", ", members.Select(TelegramDisplayName.Format));
        return $"{TelegramDisplayName.Format(message.From)} added {names}";
    }
}
