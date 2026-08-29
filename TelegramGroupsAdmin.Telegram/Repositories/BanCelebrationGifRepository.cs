using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories.Mappings;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// Repository for managing ban celebration GIFs stored in /data/media/ban-gifs/
/// Automatically converts video files (MP4, WebM) to GIF format on upload.
/// </summary>
public class BanCelebrationGifRepository : IBanCelebrationGifRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IVideoFrameExtractionService _videoService;
    private readonly ILogger<BanCelebrationGifRepository> _logger;
    private readonly string _mediaBasePath;
    private readonly HttpClient _httpClient;

    private const string GifSubdirectory = "ban-gifs";
    private const long MaxDownloadSize = 50 * 1024 * 1024; // 50 MB — matches file upload limit and Telegram API ceiling

    public BanCelebrationGifRepository(
        IDbContextFactory<AppDbContext> contextFactory,
        IVideoFrameExtractionService videoService,
        IOptions<AppOptions> appOptions,
        IHttpClientFactory httpClientFactory,
        ILogger<BanCelebrationGifRepository> logger)
    {
        _contextFactory = contextFactory;
        _videoService = videoService;
        _logger = logger;
        _mediaBasePath = Path.Combine(appOptions.Value.DataPath, "media");
        _httpClient = httpClientFactory.CreateClient();

        // Ensure the ban-gifs directory exists
        var gifDir = Path.Combine(_mediaBasePath, GifSubdirectory);
        if (!Directory.Exists(gifDir))
        {
            Directory.CreateDirectory(gifDir);
            _logger.LogInformation("Created ban celebration GIF directory: {Path}", gifDir);
        }
    }

    public async Task<List<BanCelebrationGif>> GetAllAsync(CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var dtos = await context.BanCelebrationGifs
            .OrderByDescending(g => g.CreatedAt)
            .ToListAsync(ct);

        return dtos.Select(d => d.ToModel()).ToList();
    }

    public async Task<BanCelebrationGif?> GetRandomAsync(CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        // Use SQL RANDOM() for efficient random selection
        var dto = await context.BanCelebrationGifs
            .OrderBy(_ => EF.Functions.Random())
            .FirstOrDefaultAsync(ct);

        return dto?.ToModel();
    }

    public async Task<BanCelebrationGif?> ClaimNextForCycleAsync(CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        return await RotationCycleClaim.ClaimNextAsync(
            context,
            RotationBag.BanCelebrationGifs,
            async (id, token) => (await context.BanCelebrationGifs.FindAsync([id], token))?.ToModel(),
            ct);
    }

    public async Task<BanCelebrationGif> AddFromFileAsync(Stream fileStream, string fileName, string? name, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fileStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        if (fileStream.Length == 0)
            throw new ArgumentException("File stream is empty", nameof(fileStream));

        var sourceExtension = Path.GetExtension(fileName).ToLowerInvariant();
        var isVideo = MediaUtilities.VideoExtensions.Contains(sourceExtension);

        // The file is written before the row is inserted, so the name cannot be derived from the
        // row id. Rotation claims any row whose dispensed_at is null the instant it commits, and a
        // row committed ahead of its file would be claimed, stamped, and then fail to send —
        // burning that GIF for the rest of the cycle without ever showing it. Final file is always
        // .gif; existing rows keep their older {id}.gif paths, which nothing derives or parses.
        var relativePath = $"{GifSubdirectory}/{Guid.NewGuid():N}.gif";
        var fullPath = Path.Combine(_mediaBasePath, relativePath);

        try
        {
            if (isVideo)
            {
                // Video file: save temporarily, convert to GIF, delete temp
                var tempPath = Path.Combine(
                    _mediaBasePath, GifSubdirectory, $"{Path.GetFileNameWithoutExtension(relativePath)}_temp{sourceExtension}");

                try
                {
                    // Save the uploaded video temporarily
                    await using (var tempStream = new FileStream(tempPath, FileMode.Create))
                    {
                        await fileStream.CopyToAsync(tempStream, ct);
                    }

                    // Convert video to GIF using FFmpeg
                    var success = await _videoService.ConvertVideoToGifAsync(tempPath, fullPath, maxSize: 480, ct);

                    if (!success)
                        throw new InvalidOperationException($"Failed to convert video to GIF. FFmpeg conversion failed for: {fileName}");
                }
                finally
                {
                    // Clean up temp file
                    if (File.Exists(tempPath))
                    {
                        try { File.Delete(tempPath); }
                        catch (IOException ex) { _logger.LogDebug(ex, "Failed to clean up temp file: {Path}", tempPath); }
                    }
                }
            }
            else
            {
                // Regular GIF or image: save directly
                await using (var fileStreamWrite = new FileStream(fullPath, FileMode.Create))
                {
                    await fileStream.CopyToAsync(fileStreamWrite, ct);
                }

                // Check if the saved file is actually video content despite non-video extension.
                // Giphy and similar services often serve MP4 from .gif URLs with misleading content types.
                if (MediaUtilities.IsVideoContent(fullPath))
                {
                    _logger.LogInformation(
                        "File {FileName} has non-video extension but contains video content, converting to GIF",
                        fileName);

                    var tempPath = fullPath + ".tmp";
                    File.Move(fullPath, tempPath);

                    try
                    {
                        var success = await _videoService.ConvertVideoToGifAsync(tempPath, fullPath, maxSize: 480, ct);
                        if (!success)
                            throw new InvalidOperationException(
                                $"Failed to convert video to GIF. FFmpeg conversion failed for: {fileName}");
                    }
                    finally
                    {
                        if (File.Exists(tempPath))
                        {
                            try { File.Delete(tempPath); }
                            catch (IOException ex) { _logger.LogDebug(ex, "Failed to clean up temp file: {Path}", tempPath); }
                        }
                    }

                }
            }

            // The file is on disk: the row can now be created, and is claimable the moment it commits.
            await using var context = await _contextFactory.CreateDbContextAsync(ct);

            var dto = new BanCelebrationGifDto
            {
                FilePath = relativePath,
                Name = name,
                CreatedAt = DateTimeOffset.UtcNow
            };

            context.BanCelebrationGifs.Add(dto);
            await context.SaveChangesAsync(ct);

            _logger.LogInformation("Added ban celebration GIF: {Id} ({Name}) at {Path}{ConvertedFrom}",
                dto.Id, name ?? "unnamed", relativePath,
                isVideo ? $" (converted from {sourceExtension})" : "");

            return dto.ToModel();
        }
        catch
        {
            // No row was ever written, so the file is what needs cleaning up — otherwise a failed
            // upload leaves a GIF on disk that nothing references and nothing will ever delete.
            if (File.Exists(fullPath))
            {
                try { File.Delete(fullPath); }
                catch (IOException ex) { _logger.LogDebug(ex, "Failed to clean up failed upload: {Path}", fullPath); }
            }

            throw;
        }
    }

    public async Task<BanCelebrationGif> AddFromUrlAsync(string url, string? name, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        _logger.LogInformation("Downloading ban celebration GIF from URL: {Url}", url);

        // Download the file with size limit matching file upload (50 MB)
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength > MaxDownloadSize)
            throw new InvalidOperationException(
                $"File too large: {contentLength} bytes exceeds {MaxDownloadSize / (1024 * 1024)} MB limit");

        // Determine extension from content type or URL.
        // Any video/* content type is treated as .mp4 for FFmpeg conversion,
        // since Giphy and similar services may serve video from .gif URLs.
        var extension = ".gif";
        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (contentType != null && contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
        {
            extension = ".mp4";
        }
        else if (contentType is not "image/gif")
        {
            // Unknown or missing content type — check URL extension
            var urlExtension = Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant();
            if (MediaUtilities.VideoExtensions.Contains(urlExtension))
                extension = urlExtension;
            else if (urlExtension is ".gif")
                extension = ".gif";
        }

        // Copy to a size-capped MemoryStream (guards against servers that omit Content-Length)
        await using var downloadStream = await response.Content.ReadAsStreamAsync(ct);
        using var cappedStream = new MemoryStream();
        var buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;
        while ((bytesRead = await downloadStream.ReadAsync(buffer, ct)) > 0)
        {
            totalRead += bytesRead;
            if (totalRead > MaxDownloadSize)
                throw new InvalidOperationException(
                    $"Download exceeded {MaxDownloadSize / (1024 * 1024)} MB limit");
            cappedStream.Write(buffer, 0, bytesRead);
        }

        cappedStream.Position = 0;
        return await AddFromFileAsync(cappedStream, $"download{extension}", name, ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var dto = await context.BanCelebrationGifs.FindAsync([id], ct);

        if (dto == null)
        {
            _logger.LogWarning("Attempted to delete non-existent ban celebration GIF: {Id}", id);
            return;
        }

        // Delete the GIF file from disk
        var fullPath = Path.Combine(_mediaBasePath, dto.FilePath);
        if (File.Exists(fullPath))
        {
            try
            {
                File.Delete(fullPath);
                _logger.LogInformation("Deleted ban celebration GIF file: {Path}", fullPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete ban celebration GIF file: {Path}", fullPath);
            }
        }

        // Delete the thumbnail file from disk
        if (!string.IsNullOrEmpty(dto.ThumbnailPath))
        {
            var thumbFullPath = Path.Combine(_mediaBasePath, dto.ThumbnailPath);
            if (File.Exists(thumbFullPath))
            {
                try
                {
                    File.Delete(thumbFullPath);
                    _logger.LogInformation("Deleted ban celebration GIF thumbnail: {Path}", thumbFullPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete ban celebration GIF thumbnail: {Path}", thumbFullPath);
                }
            }
        }

        // Delete the database record
        context.BanCelebrationGifs.Remove(dto);
        await context.SaveChangesAsync(ct);

        _logger.LogInformation("Deleted ban celebration GIF record: {Id}", id);
    }

    public async Task UpdateFileIdAsync(int id, string fileId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);

        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var dto = await context.BanCelebrationGifs.FindAsync([id], ct);

        if (dto == null)
        {
            _logger.LogWarning("Attempted to update file_id for non-existent GIF: {Id}", id);
            return;
        }

        dto.FileId = fileId;
        await context.SaveChangesAsync(ct);

        _logger.LogDebug("Cached Telegram file_id for GIF {Id}: {FileId}", id, fileId);
    }

    public async Task ClearFileIdAsync(int id, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        await context.BanCelebrationGifs
            .Where(g => g.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.FileId, (string?)null), ct);

        _logger.LogInformation("Cleared stale Telegram file_id for GIF {Id}", id);
    }

    public async Task UpdateThumbnailPathAsync(int id, string thumbnailPath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(thumbnailPath);

        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var dto = await context.BanCelebrationGifs.FindAsync([id], ct);

        if (dto == null)
        {
            _logger.LogWarning("Attempted to update thumbnail_path for non-existent GIF: {Id}", id);
            return;
        }

        dto.ThumbnailPath = thumbnailPath;
        await context.SaveChangesAsync(ct);

        _logger.LogDebug("Set thumbnail path for GIF {Id}: {Path}", id, thumbnailPath);
    }

    public async Task UpdatePhotoHashAsync(int id, byte[] photoHash, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(photoHash);

        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        await context.BanCelebrationGifs
            .Where(g => g.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.PhotoHash, photoHash), ct);

        _logger.LogDebug("Updated photo hash for GIF {Id}", id);
    }

    public async Task<BanCelebrationGif?> FindSimilarAsync(byte[] photoHash, int maxHammingDistance = 8, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(photoHash);

        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        // Load all GIFs that have a photo hash - in a small library this is efficient
        var gifsWithHash = await context.BanCelebrationGifs
            .Where(g => g.PhotoHash != null)
            .ToListAsync(ct);

        // Compare hashes using Hamming distance (number of different bits)
        foreach (var gif in gifsWithHash)
        {
            var distance = BitwiseUtilities.HammingDistance(photoHash, gif.PhotoHash!);
            if (distance <= maxHammingDistance)
            {
                _logger.LogDebug("Found similar GIF: {Id} with Hamming distance {Distance}", gif.Id, distance);
                return gif.ToModel();
            }
        }

        return null;
    }

    public string GetFullPath(string relativePath)
        => Path.Combine(_mediaBasePath, relativePath);

    public async Task<int> GetCountAsync(CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        return await context.BanCelebrationGifs.CountAsync(ct);
    }
}
