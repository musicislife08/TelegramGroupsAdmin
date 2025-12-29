namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Response from manual message translation endpoint.
/// </summary>
public record TranslateMessageResponse : IApiResponse
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? TranslatedText { get; init; }
    public string? DetectedLanguage { get; init; }

    public static TranslateMessageResponse Ok(string translatedText, string detectedLanguage) =>
        new()
        {
            Success = true,
            TranslatedText = translatedText,
            DetectedLanguage = detectedLanguage
        };

    public static TranslateMessageResponse Fail(string error) =>
        new() { Success = false, Error = error };
}
