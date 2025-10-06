namespace TgSpam_PreFilterApi.Services.Vision;

public interface IVisionSpamDetectionService
{
    Task<CheckResult> AnalyzeImageAsync(Stream imageStream, string? messageText, CancellationToken ct = default);
}
