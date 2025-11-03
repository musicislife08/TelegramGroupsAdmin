using System.Collections.Concurrent;
using Markdig;
using Markdown.ColorCode;
using TelegramGroupsAdmin.Models.Help;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TelegramGroupsAdmin.Services.Help;

/// <summary>
/// In-memory cache of compiled help documentation loaded at startup
/// </summary>
public class HelpDocumentService : IHelpDocumentService
{
    private readonly ConcurrentDictionary<string, HelpDocument> _documents = new();
    private readonly MarkdownPipeline _markdownPipeline;
    private readonly IDeserializer _yamlDeserializer;
    private readonly ILogger<HelpDocumentService> _logger;

    public bool IsInitialized => _documents.Count > 0;

    public HelpDocumentService(ILogger<HelpDocumentService> logger)
    {
        _logger = logger;

        // Configure Markdig with GitHub Flavored Markdown + syntax highlighting
        _markdownPipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions() // Tables, task lists, auto-identifiers, etc.
            .UseAutoIdentifiers() // Auto-generate IDs for headings
            .UseGenericAttributes() // Allow {#id .class} syntax
            .UseColorCode() // Code block syntax highlighting via Markdown.ColorCode
            .Build();

        // Configure YAML deserializer for front-matter
        _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(PascalCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    /// <summary>
    /// Load and compile all markdown files from the Help directory (called by startup service)
    /// </summary>
    public void LoadDocuments(string helpDirectoryPath)
    {
        if (!Directory.Exists(helpDirectoryPath))
        {
            _logger.LogWarning("Help directory not found: {Path}", helpDirectoryPath);
            return;
        }

        var markdownFiles = Directory.GetFiles(helpDirectoryPath, "*.md", SearchOption.AllDirectories);
        _logger.LogInformation("Found {Count} markdown help files in {Path}", markdownFiles.Length, helpDirectoryPath);

        foreach (var filePath in markdownFiles)
        {
            try
            {
                var document = CompileDocument(filePath, helpDirectoryPath);
                _documents[document.Slug] = document;
                _logger.LogDebug("Loaded help document: {Slug} ({Title})", document.Slug, document.Metadata.Title);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to compile help document: {Path}", filePath);
            }
        }

        _logger.LogInformation("Successfully loaded {Count} help documents", _documents.Count);
    }

    private HelpDocument CompileDocument(string filePath, string baseDirectory)
    {
        var markdown = File.ReadAllText(filePath);
        var relativePath = Path.GetRelativePath(baseDirectory, filePath);
        var slug = GenerateSlug(relativePath);

        // Extract YAML front-matter if present
        var (metadata, contentMarkdown) = ExtractFrontMatter(markdown);

        // Render markdown to HTML
        var html = Markdig.Markdown.ToHtml(contentMarkdown, _markdownPipeline);

        // Generate breadcrumbs from folder structure
        var breadcrumbs = GenerateBreadcrumbs(relativePath);

        return new HelpDocument
        {
            Slug = slug,
            Metadata = metadata,
            HtmlContent = html,
            MarkdownSource = contentMarkdown,
            RelativePath = relativePath,
            Breadcrumbs = breadcrumbs
        };
    }

    private (HelpMetadata metadata, string contentMarkdown) ExtractFrontMatter(string markdown)
    {
        var metadata = new HelpMetadata();

        // Check for YAML front-matter (--- at start)
        if (!markdown.StartsWith("---"))
        {
            return (metadata, markdown);
        }

        var endOfFrontMatter = markdown.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (endOfFrontMatter == -1)
        {
            return (metadata, markdown);
        }

        try
        {
            var yamlContent = markdown[3..endOfFrontMatter].Trim();
            metadata = _yamlDeserializer.Deserialize<HelpMetadata>(yamlContent) ?? new HelpMetadata();

            var contentStart = endOfFrontMatter + 4; // Skip past "\n---"
            var contentMarkdown = markdown[contentStart..].TrimStart();
            return (metadata, contentMarkdown);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse YAML front-matter, using defaults");
            return (metadata, markdown);
        }
    }

    private string GenerateSlug(string relativePath)
    {
        // Convert "spam-detection.md" → "spam-detection"
        // Convert "algorithms/similarity.md" → "algorithms/similarity"
        var slug = relativePath
            .Replace(".md", "", StringComparison.OrdinalIgnoreCase)
            .Replace('\\', '/'); // Normalize Windows paths

        return slug;
    }

    private List<BreadcrumbItem> GenerateBreadcrumbs(string relativePath)
    {
        var breadcrumbs = new List<BreadcrumbItem>
        {
            new() { Text = "Help", Href = "/help", Disabled = false }
        };

        var parts = relativePath.Replace('\\', '/').Split('/');

        // Build breadcrumb path for nested folders
        var currentPath = "";
        for (var i = 0; i < parts.Length - 1; i++) // Skip the filename itself
        {
            var part = parts[i];
            currentPath = string.IsNullOrEmpty(currentPath) ? part : $"{currentPath}/{part}";

            breadcrumbs.Add(new BreadcrumbItem
            {
                Text = FormatBreadcrumbText(part),
                Href = $"/help/{currentPath}",
                Disabled = false
            });
        }

        // Add current page (disabled, no link)
        var filename = Path.GetFileNameWithoutExtension(parts[^1]);
        breadcrumbs.Add(new BreadcrumbItem
        {
            Text = FormatBreadcrumbText(filename),
            Href = null,
            Disabled = true
        });

        return breadcrumbs;
    }

    private string FormatBreadcrumbText(string text)
    {
        // Convert "spam-detection" → "Spam Detection"
        return string.Join(" ", text.Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpper(word[0]) + word[1..]));
    }

    public HelpDocument? GetDocument(string slug)
    {
        _documents.TryGetValue(slug, out var document);
        return document;
    }

    public IReadOnlyList<HelpDocument> GetAllDocuments()
    {
        return _documents.Values
            .Where(d => d.Metadata.ShowInIndex)
            .OrderBy(d => d.Metadata.Order)
            .ThenBy(d => d.Metadata.Title)
            .ToList();
    }

    public IReadOnlyList<HelpDocument> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return GetAllDocuments();
        }

        var lowerQuery = query.ToLowerInvariant();

        return _documents.Values
            .Where(d => d.Metadata.ShowInIndex &&
                       (d.Metadata.Title.Contains(lowerQuery, StringComparison.OrdinalIgnoreCase) ||
                        d.Metadata.Description.Contains(lowerQuery, StringComparison.OrdinalIgnoreCase) ||
                        d.Metadata.SearchKeywords.Contains(lowerQuery, StringComparison.OrdinalIgnoreCase) ||
                        d.MarkdownSource.Contains(lowerQuery, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(d => d.Metadata.Order)
            .ThenBy(d => d.Metadata.Title)
            .ToList();
    }

    public IReadOnlyList<HelpDocument> GetDocumentsByCategory(string? category)
    {
        return _documents.Values
            .Where(d => d.Metadata.ShowInIndex &&
                       string.Equals(d.Metadata.Category, category, StringComparison.OrdinalIgnoreCase))
            .OrderBy(d => d.Metadata.Order)
            .ThenBy(d => d.Metadata.Title)
            .ToList();
    }
}
