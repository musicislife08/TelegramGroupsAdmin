using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories.Mappings;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// Repository for managing ban celebration GIFs stored in /data/media/ban-gifs/
/// </summary>
public class BanCelebrationGifRepository : IBanCelebrationGifRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<BanCelebrationGifRepository> _logger;
    private readonly string _mediaBasePath;
    private readonly HttpClient _httpClient;

    private const string GifSubdirectory = "ban-gifs";

    public BanCelebrationGifRepository(
        IDbContextFactory<AppDbContext> contextFactory,
        IOptions<MessageHistoryOptions> historyOptions,
        IHttpClientFactory httpClientFactory,
        ILogger<BanCelebrationGifRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _mediaBasePath = Path.Combine(historyOptions.Value.ImageStoragePath, "media");
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

    public async Task<BanCelebrationGif?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var dto = await context.BanCelebrationGifs.FindAsync([id], ct);
        return dto?.ToModel();
    }

    public async Task<BanCelebrationGif> AddFromFileAsync(Stream fileStream, string fileName, string? name, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fileStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        if (fileStream.Length == 0)
            throw new ArgumentException("File stream is empty", nameof(fileStream));

        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        // Create the database record first to get the ID
        var dto = new BanCelebrationGifDto
        {
            FilePath = string.Empty, // Will be set after we have the ID
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow
        };

        context.BanCelebrationGifs.Add(dto);
        await context.SaveChangesAsync(ct);

        // Now save the file using the ID
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (string.IsNullOrEmpty(extension))
            extension = ".gif";

        var relativePath = $"{GifSubdirectory}/{dto.Id}{extension}";
        var fullPath = Path.Combine(_mediaBasePath, relativePath);

        await using (var fileStreamWrite = new FileStream(fullPath, FileMode.Create))
        {
            await fileStream.CopyToAsync(fileStreamWrite, ct);
        }

        // Update the file path
        dto.FilePath = relativePath;
        await context.SaveChangesAsync(ct);

        _logger.LogInformation("Added ban celebration GIF: {Id} ({Name}) at {Path}", dto.Id, name ?? "unnamed", relativePath);

        return dto.ToModel();
    }

    public async Task<BanCelebrationGif> AddFromUrlAsync(string url, string? name, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        _logger.LogInformation("Downloading ban celebration GIF from URL: {Url}", url);

        // Download the file
        using var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        // Determine extension from content type or URL
        var extension = ".gif";
        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (contentType == "video/mp4")
            extension = ".mp4";
        else if (contentType == "image/gif")
            extension = ".gif";
        else
        {
            // Try to get from URL
            var urlExtension = Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant();
            if (urlExtension is ".gif" or ".mp4")
                extension = urlExtension;
        }

        await using var downloadStream = await response.Content.ReadAsStreamAsync(ct);
        return await AddFromFileAsync(downloadStream, $"download{extension}", name, ct);
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

    public string GetFullPath(string relativePath)
        => Path.Combine(_mediaBasePath, relativePath);

    public async Task<int> GetCountAsync(CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        return await context.BanCelebrationGifs.CountAsync(ct);
    }
}
