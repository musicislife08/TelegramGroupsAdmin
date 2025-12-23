namespace TelegramGroupsAdmin.ContentDetection.Constants;

/// <summary>
/// Constants for Bayesian spam classifier.
/// </summary>
public static class BayesConstants
{
    /// <summary>
    /// Uncertainty lower bound - below 40% probability, abstain (likely ham)
    /// </summary>
    public const int UncertaintyLowerBound = 40;

    /// <summary>
    /// Uncertainty upper bound - 40-60% probability range is uncertain, abstain
    /// </summary>
    public const int UncertaintyUpperBound = 60;

    /// <summary>
    /// Probability threshold for 99%+ confidence (5.0 points)
    /// </summary>
    public const int ProbabilityThreshold99 = 99;

    /// <summary>
    /// Probability threshold for 95-99% confidence (3.5 points)
    /// </summary>
    public const int ProbabilityThreshold95 = 95;

    /// <summary>
    /// Probability threshold for 80-95% confidence (2.0 points)
    /// </summary>
    public const int ProbabilityThreshold80 = 80;

    /// <summary>
    /// Probability threshold for 70-80% confidence (1.0 point)
    /// </summary>
    public const int ProbabilityThreshold70 = 70;
}
