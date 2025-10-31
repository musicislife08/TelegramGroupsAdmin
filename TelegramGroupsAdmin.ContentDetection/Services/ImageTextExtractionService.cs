using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Extracts text from images using Tesseract OCR.
/// Used for image spam detection (ML-5) and video spam detection (ML-6).
/// </summary>
public interface IImageTextExtractionService
{
    /// <summary>
    /// Extracts text from an image file using OCR.
    /// </summary>
    /// <param name="imagePath">Full path to image file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Extracted text, or empty string if no text detected</returns>
    Task<string> ExtractTextAsync(string imagePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether OCR is available (tesseract binary found on startup).
    /// If false, ExtractTextAsync will always return empty string.
    /// </summary>
    bool IsAvailable { get; }
}

/// <summary>
/// Result of Tesseract OCR extraction including confidence score
/// </summary>
public record OcrResult(string Text, double Confidence);

public class ImageTextExtractionService : IImageTextExtractionService
{
    private readonly ILogger<ImageTextExtractionService> _logger;
    private readonly string _tessDataPath;
    private readonly string? _tesseractPath;

    public bool IsAvailable => _tesseractPath != null;

    public ImageTextExtractionService(ILogger<ImageTextExtractionService> logger)
    {
        _logger = logger;

        // Tesseract data files location
        // In production (Docker): /tessdata (baked into image)
        // In development: ./tessdata (local for testing)
        // Can be overridden via TESSDATA_PREFIX environment variable for custom languages
        _tessDataPath = Environment.GetEnvironmentVariable("TESSDATA_PREFIX")
                       ?? (Directory.Exists("/tessdata") ? "/tessdata" : "./tessdata");

        // Detect tesseract binary on startup
        // Allow override via TESSERACT_PATH environment variable for non-standard installations
        _tesseractPath = FindTesseractBinary();

        if (_tesseractPath != null)
        {
            _logger.LogInformation(
                "Tesseract OCR initialized: binary={TesseractPath}, tessdata={TessDataPath}",
                _tesseractPath, _tessDataPath);
        }
        else
        {
            _logger.LogWarning(
                "Tesseract OCR NOT available - 'tesseract' binary not found in PATH. " +
                "ML-5 Layer 2 (OCR + text spam checks) will be skipped. " +
                "Install: Docker='apt-get install tesseract-ocr', Mac='brew install tesseract'. " +
                "Or set TESSERACT_PATH environment variable to specify custom binary location.");
        }
    }

    public async Task<string> ExtractTextAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        // Graceful degradation: if tesseract not available, skip OCR layer
        if (_tesseractPath == null)
        {
            _logger.LogDebug("Skipping OCR for {ImagePath} - tesseract binary not available",
                Path.GetFileName(imagePath));
            return string.Empty;
        }

        try
        {
            if (!File.Exists(imagePath))
            {
                _logger.LogWarning("Image file not found: {ImagePath}", imagePath);
                return string.Empty;
            }

            var result = await ExtractTextWithConfidenceAsync(imagePath, cancellationToken);

            _logger.LogDebug(
                "OCR extracted {CharCount} characters with {Confidence:P1} confidence from {ImagePath}",
                result.Text.Length, result.Confidence, Path.GetFileName(imagePath));

            return result.Text;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OCR failed for image: {ImagePath}", imagePath);
            return string.Empty; // Fail gracefully - return empty string so spam check continues
        }
    }

    /// <summary>
    /// Extract text and confidence score from image using Tesseract CLI
    /// </summary>
    private async Task<OcrResult> ExtractTextWithConfidenceAsync(
        string imagePath,
        CancellationToken cancellationToken)
    {
        // Call tesseract with TSV output format to get confidence scores
        // Command: tesseract image.jpg stdout -l eng tsv
        var startInfo = new ProcessStartInfo
        {
            FileName = _tesseractPath,
            Arguments = $"\"{imagePath}\" stdout -l eng tsv",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Set TESSDATA_PREFIX environment variable for the process
        startInfo.Environment["TESSDATA_PREFIX"] = _tessDataPath;

        using var process = new Process { StartInfo = startInfo };
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) outputBuilder.AppendLine(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) errorBuilder.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Wait for process to complete with timeout (30 seconds)
        await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);

        if (process.ExitCode != 0)
        {
            var error = errorBuilder.ToString();
            _logger.LogWarning("Tesseract exited with code {ExitCode}: {Error}",
                process.ExitCode, error);
            return new OcrResult(string.Empty, 0.0);
        }

        // Parse TSV output (format: level page_num block_num par_num line_num word_num left top width height conf text)
        var tsvOutput = outputBuilder.ToString();
        return ParseTsvOutput(tsvOutput);
    }

    /// <summary>
    /// Parse Tesseract TSV output to extract text and calculate mean confidence
    /// </summary>
    private OcrResult ParseTsvOutput(string tsvOutput)
    {
        var lines = tsvOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length <= 1) // Header only or empty
        {
            return new OcrResult(string.Empty, 0.0);
        }

        var words = new List<string>();
        var confidences = new List<double>();

        // Skip header line (index 0)
        for (int i = 1; i < lines.Length; i++)
        {
            var columns = lines[i].Split('\t');
            if (columns.Length < 12) continue; // Invalid line

            // Column 10 = confidence, Column 11 = text
            var confString = columns[10];
            var text = columns[11].Trim();

            // Skip empty words and "-1" confidence (means no text detected at that level)
            if (string.IsNullOrWhiteSpace(text) || confString == "-1") continue;

            if (double.TryParse(confString, NumberStyles.Float, CultureInfo.InvariantCulture, out var confidence))
            {
                words.Add(text);
                confidences.Add(confidence);
            }
        }

        if (words.Count == 0)
        {
            return new OcrResult(string.Empty, 0.0);
        }

        var extractedText = string.Join(" ", words);
        var meanConfidence = confidences.Average() / 100.0; // Tesseract returns 0-100, convert to 0.0-1.0

        return new OcrResult(extractedText, meanConfidence);
    }

    /// <summary>
    /// Find tesseract binary in PATH or common install locations
    /// Supports TESSERACT_PATH environment variable override
    /// </summary>
    private string? FindTesseractBinary()
    {
        // Priority 1: Check TESSERACT_PATH environment variable override
        var envPath = Environment.GetEnvironmentVariable("TESSERACT_PATH");
        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
        {
            return envPath;
        }

        // Priority 2: Try common binary names
        var binaryNames = OperatingSystem.IsWindows()
            ? new[] { "tesseract.exe" }
            : new[] { "tesseract" };

        foreach (var binaryName in binaryNames)
        {
            // Priority 3: Check if it's in PATH
            var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            var searchPaths = pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

            foreach (var searchPath in searchPaths)
            {
                var fullPath = Path.Combine(searchPath, binaryName);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }

            // Priority 4: Check common install locations (fallback if not in PATH)
            var commonPaths = OperatingSystem.IsMacOS()
                ? ["/opt/homebrew/bin/tesseract", "/usr/local/bin/tesseract"]
                : OperatingSystem.IsLinux()
                    ? new[] { "/usr/bin/tesseract", "/usr/local/bin/tesseract" }
                    : new[] { @"C:\Program Files\Tesseract-OCR\tesseract.exe" };

            foreach (var commonPath in commonPaths)
            {
                if (File.Exists(commonPath))
                {
                    return commonPath;
                }
            }
        }

        return null;
    }
}
