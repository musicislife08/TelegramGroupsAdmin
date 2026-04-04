using Microsoft.Extensions.Logging;

namespace TelegramGroupsAdmin.Telegram.Services.BackgroundServices;

public partial class MessageProcessingService
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Processed open-ended exam answer for {User}: Complete={Complete}, Passed={Passed}")]
    private static partial void LogOpenEndedExamAnswer(ILogger logger, string user, bool complete, bool? passed);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Detected service message: Type={Type}, MessageId={MessageId}, Chat={Chat}")]
    private static partial void LogDetectedServiceMessage(ILogger logger, string type, int messageId, string chat);

    [LoggerMessage(Level = LogLevel.Information, Message = "Skipping deletion of LeftChatMember service message - bot was removed from {Chat}")]
    private static partial void LogSkippingBotRemovedDeletion(ILogger logger, string chat);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted service message (type: {Type}) in {Chat}")]
    private static partial void LogDeletedServiceMessage(ILogger logger, string type, string chat);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Skipping deletion of {Type} service message (disabled in config) in {Chat}")]
    private static partial void LogSkippingServiceMessageDeletion(ILogger logger, string type, string chat);

    [LoggerMessage(Level = LogLevel.Information, Message = "Refreshing admin cache for {Chat} ({Reason})")]
    private static partial void LogRefreshingAdminCache(ILogger logger, string chat, string reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Discovered new private {Chat}, skipping admin cache refresh")]
    private static partial void LogDiscoveredNewPrivateChat(ILogger logger, string chat);

    [LoggerMessage(Level = LogLevel.Information, Message = "Profile change detected for {User}: {OldProfile} → {NewProfile}, scheduling background scan")]
    private static partial void LogProfileChangeDetected(ILogger logger, string user, string oldProfile, string newProfile);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cached message {MessageId} from {User} in {Chat} (photo: {HasPhoto}, text: {HasText})")]
    private static partial void LogCachedMessage(ILogger logger, int messageId, string user, string chat, bool hasPhoto, bool hasText);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Deleted command message {MessageId} in {Chat}")]
    private static partial void LogDeletedCommandMessage(ILogger logger, int messageId, string chat);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Skipping content detection for inactive {Chat} - bot is not admin")]
    private static partial void LogSkippingContentDetectionInactiveChat(ILogger logger, string chat);
}
