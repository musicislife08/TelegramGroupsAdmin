using Microsoft.Extensions.Logging;
using Panlingo.LanguageIdentification.FastText;

namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// FastText-based language detection service using Facebook's 176-language model.
/// Thread-safe singleton implementation.
/// Gracefully degrades if model file not found (returns null, logs warning).
///
/// Memory footprint: Model file is 916 KB on disk, approximately 30-50 MB in memory when loaded.
/// Loaded once at startup (singleton) and reused for all language detection requests.
/// Acceptable for homelab deployment with typical available memory (2-8 GB).
/// </summary>
public class FastTextLanguageDetectionService : ILanguageDetectionService, IDisposable
{
    private readonly ILogger<FastTextLanguageDetectionService> _logger;
    private readonly FastTextDetector? _detector;
    private readonly bool _isLoaded;

    public FastTextLanguageDetectionService(ILogger<FastTextLanguageDetectionService> logger)
    {
        _logger = logger;

        // Try to load model from /lang-models (Docker) or TelegramGroupsAdmin/lang-models (local dev)
        // Same pattern as tessdata: baked into image, not in /data volume
        var modelPaths = new[]
        {
            "/lang-models/lid.176.ftz",                    // Docker production path (baked into image)
            "TelegramGroupsAdmin/lang-models/lid.176.ftz"  // Local development path
        };

        foreach (var modelPath in modelPaths)
        {
            if (File.Exists(modelPath))
            {
                try
                {
                    _logger.LogInformation("Loading FastText language model from {ModelPath}", modelPath);
                    _detector = new FastTextDetector();
                    _detector.LoadModel(modelPath);
                    _isLoaded = true;
                    _logger.LogInformation("FastText language model loaded successfully (176 languages)");
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load FastText model from {ModelPath}", modelPath);
                }
            }
        }

        // Model not found - graceful degradation
        _logger.LogWarning(
            "FastText model not found at any of these paths: {Paths}. Language detection will fall back to Latin script ratio.",
            string.Join(", ", modelPaths));
        _isLoaded = false;
    }

    /// <summary>
    /// Detect language using FastText model.
    /// Returns null if model not loaded or text is too short.
    /// </summary>
    public (string languageCode, double confidence)? DetectLanguage(string text)
    {
        if (!_isLoaded || _detector == null)
        {
            return null;  // Model not loaded, caller should fall back
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // FastText works best with at least a few words
        // For very short text (<10 chars), return null to fall back to OpenAI
        if (text.Length < 10)
        {
            _logger.LogDebug("Text too short for FastText ({Length} chars), falling back", text.Length);
            return null;
        }

        try
        {
            // Predict language with top-1 result
            var prediction = _detector.Predict(text, 1);

            if (prediction == null || !prediction.Any())
            {
                _logger.LogWarning("FastText returned no predictions for text: {TextPreview}",
                    text.Length > 50 ? text[..50] + "..." : text);
                return null;
            }

            var topPrediction = prediction.FirstOrDefault();
            if (topPrediction == null)
            {
                return null;
            }

            var languageCode = topPrediction.Label.Replace("__label__", "");  // FastText prefixes labels
            var confidence = topPrediction.Probability;

            _logger.LogDebug("FastText detected language: {Language} (confidence: {Confidence:F2})",
                languageCode, confidence);

            return (languageCode, confidence);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during FastText language detection");
            return null;
        }
    }

    public void Dispose()
    {
        _detector?.Dispose();
    }
}
