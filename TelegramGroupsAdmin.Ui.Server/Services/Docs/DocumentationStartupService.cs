namespace TelegramGroupsAdmin.Ui.Server.Services.Docs;

/// <summary>
/// Background service that loads folder-based documentation at application startup
/// </summary>
public class DocumentationStartupService : IHostedService
{
    private readonly ILogger<DocumentationStartupService> _logger;
    private readonly DocumentationService _documentationService;
    private readonly IWebHostEnvironment _environment;

    public DocumentationStartupService(
        ILogger<DocumentationStartupService> logger,
        IDocumentationService documentationService,
        IWebHostEnvironment environment)
    {
        _logger = logger;
        _documentationService = (DocumentationService)documentationService; // Safe cast, registered as singleton
        _environment = environment;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var docsDirectory = Path.Combine(_environment.ContentRootPath, "Docs");
            _documentationService.LoadDocuments(docsDirectory);

            if (!_documentationService.IsInitialized)
            {
                _logger.LogWarning("No documentation pages were loaded. Documentation system will show empty results.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load documentation during startup");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
