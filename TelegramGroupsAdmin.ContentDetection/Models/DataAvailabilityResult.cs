namespace TelegramGroupsAdmin.ContentDetection.Models;

public sealed record DataAvailabilityResult(int SpamSampleCount, int LegitMessageCount, int DetectionResultCount, string? ValidationMessage);
