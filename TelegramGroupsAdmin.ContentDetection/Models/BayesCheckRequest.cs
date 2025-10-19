namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Request for Bayes spam check
/// </summary>
public sealed class BayesCheckRequest : ContentCheckRequestBase
{
    public required int MinMessageLength { get; init; }
    public required int MinSpamProbability { get; init; }
}
