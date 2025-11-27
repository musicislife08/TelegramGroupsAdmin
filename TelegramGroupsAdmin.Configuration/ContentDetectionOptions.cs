namespace TelegramGroupsAdmin.Configuration;

public sealed class ContentDetectionOptions
{
    public int TimeoutSeconds { get; set; } = 30;
    public int ImageLookupRetryDelayMs { get; set; } = 100;
    public int MinConfidenceThreshold { get; set; } = 85;
    public string? ApiKey { get; set; }
}
