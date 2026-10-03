using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>One message's current verdict and current text length, for auto-trust.</summary>
public sealed record UserMessageVerdict(int MessageId, long ChatId, VerdictClassification Classification, bool IsSpam, int TextLength);
