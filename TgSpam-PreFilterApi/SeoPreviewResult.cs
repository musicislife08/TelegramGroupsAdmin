namespace TgSpam_PreFilterApi;

public record SeoPreviewResult(
    string? Title,
    string? Description,
    string? OgTitle,
    string? OgDescription,
    string? OgImage,
    string? FinalUrl);