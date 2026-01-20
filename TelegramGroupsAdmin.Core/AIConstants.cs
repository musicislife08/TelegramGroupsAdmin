namespace TelegramGroupsAdmin.Core;

/// <summary>
/// Centralized constants for AI service configuration and token limits.
/// </summary>
public static class AIConstants
{
    /// <summary>
    /// Default maximum tokens for AI feature tests.
    /// Used when testing AI provider connections to validate configuration.
    /// </summary>
    public const int DefaultFeatureTestMaxTokens = 500;

    /// <summary>
    /// Expected maximum number of cached AI kernels in homelab deployment.
    /// Cache size is small (connections × models configured).
    /// </summary>
    public const int ExpectedMaxCachedKernels = 10;
}
