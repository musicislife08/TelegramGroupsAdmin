namespace TelegramGroupsAdmin.Ui.Server.Services;

public record SeoPreviewResult(
    string? Title,
    string? Description,
    string? OgTitle,
    string? OgDescription,
    string? OgImage,
    string? FinalUrl);