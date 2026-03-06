namespace TelegramGroupsAdmin.ContentDetection.ML;

/// <summary>
/// Result of Bayes classification for a single message.
/// </summary>
public sealed record BayesClassificationResult(
    double SpamProbability,
    string Details,
    double Certainty
);
