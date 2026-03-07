namespace TelegramGroupsAdmin.Configuration;

public sealed class ContentDetectionOptions
{
    public int TimeoutSeconds { get; set; } = 30;
    public int ImageLookupRetryDelayMs { get; set; } = 100;
    public double MinConfidenceThreshold { get; set; } = 4.25;
    public string? ApiKey { get; set; }
}
