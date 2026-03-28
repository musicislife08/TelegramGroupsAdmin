using Microsoft.Extensions.Logging;

namespace TelegramGroupsAdmin.ContentDetection.Services;

public partial class ContentDetectionEngineV2
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "Running AI veto check for {User} (custom prompt: {HasCustom})")]
    private static partial void LogRunningAIVetoCheck(ILogger logger, string user, bool hasCustom);

    [LoggerMessage(Level = LogLevel.Information, Message = "AI vetoed spam detection for {User} (clean result with 0.0 score)")]
    private static partial void LogAIVetoedSpamDetection(ILogger logger, string user);

    [LoggerMessage(Level = LogLevel.Debug, Message = "AI confirmed spam for {User} with score {Score}")]
    private static partial void LogAIConfirmedSpam(ILogger logger, string user, double score);
}
