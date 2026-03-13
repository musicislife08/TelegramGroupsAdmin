namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Result of Tesseract OCR extraction including confidence score
/// </summary>
public record OcrResult(string Text, double Confidence);
