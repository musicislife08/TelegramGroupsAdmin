namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Request for channel reply spam signal check.
/// No additional properties needed — the check reads IsReplyToChannelPost from metadata
/// on the parent ContentCheckRequest during ShouldExecute.
/// </summary>
public sealed class ChannelReplyCheckRequest : ContentCheckRequestBase;
