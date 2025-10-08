namespace TelegramGroupsAdmin.Configuration;

public sealed record SpamDetectionOptions
{
    public int TimeoutSeconds { get; init; } = 30;
    public int ImageLookupRetryDelayMs { get; init; } = 100;
    public int MinConfidenceThreshold { get; init; } = 85;
    public string? ApiKey { get; init; }
}
