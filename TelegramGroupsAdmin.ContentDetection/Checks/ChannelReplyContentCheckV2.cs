using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Abstractions;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Checks;

/// <summary>
/// V2 channel reply check with proper abstention.
/// Adds a soft spam signal when a message replies to a channel post
/// (linked channel system post or anonymous admin posting as the group).
/// Scoring: 0.8 points when detected (same weight as formatting anomaly).
/// </summary>
public class ChannelReplyContentCheckV2(ILogger<ChannelReplyContentCheckV2> logger) : IContentCheckV2
{
    public CheckName CheckName => CheckName.ChannelReply;

    public bool ShouldExecute(ContentCheckRequest request)
    {
        // Only relevant when the message is a reply to a channel post
        if (!request.Metadata.IsReplyToChannelPost)
            return false;

        // Skip for trusted/admin users (not a critical check)
        if (request.IsUserTrusted || request.IsUserAdmin)
        {
            logger.LogDebug(
                "Skipping ChannelReply check for user {UserId}: User is {UserType}",
                request.User.Id,
                request.IsUserTrusted ? "trusted" : "admin");
            return false;
        }

        return true;
    }

    public ValueTask<ContentCheckResponseV2> CheckAsync(ContentCheckRequestBase request)
    {
        var startTimestamp = Stopwatch.GetTimestamp();

        // If we got here, ShouldExecute already confirmed IsReplyToChannelPost is true
        return ValueTask.FromResult(new ContentCheckResponseV2
        {
            CheckName = CheckName,
            Score = ScoringConstants.ScoreChannelReply,
            Abstained = false,
            Details = "Message is a reply to a channel post",
            ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
        });
    }
}
