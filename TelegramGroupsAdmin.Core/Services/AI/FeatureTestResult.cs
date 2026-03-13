namespace TelegramGroupsAdmin.Core.Services.AI;

/// <summary>
/// Result of a feature test
/// </summary>
/// <param name="Success">Whether the test passed</param>
/// <param name="Message">Human-readable result message</param>
/// <param name="ErrorDetails">Technical error details (for logging/debugging)</param>
public record FeatureTestResult(bool Success, string Message, string? ErrorDetails = null)
{
    public static FeatureTestResult Ok(string message) => new(true, message);
    public static FeatureTestResult Fail(string message, string? details = null) => new(false, message, details);
}
