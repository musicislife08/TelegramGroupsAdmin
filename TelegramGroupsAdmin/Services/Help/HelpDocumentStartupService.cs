namespace TelegramGroupsAdmin.Services.Help;

/// <summary>
/// Background service that loads help documentation at application startup
/// </summary>
public class HelpDocumentStartupService : IHostedService
{
    private readonly ILogger<HelpDocumentStartupService> _logger;
    private readonly HelpDocumentService _helpDocumentService;
    private readonly IWebHostEnvironment _environment;

    public HelpDocumentStartupService(
        ILogger<HelpDocumentStartupService> logger,
        IHelpDocumentService helpDocumentService,
        IWebHostEnvironment environment)
    {
        _logger = logger;
        _helpDocumentService = (HelpDocumentService)helpDocumentService; // Safe cast, registered as singleton
        _environment = environment;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var helpDirectory = Path.Combine(_environment.ContentRootPath, "Help");
            _logger.LogInformation("Loading help documentation from: {Path}", helpDirectory);

            _helpDocumentService.LoadDocuments(helpDirectory);

            if (!_helpDocumentService.IsInitialized)
            {
                _logger.LogWarning("No help documents were loaded. Help system will show empty results.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load help documentation during startup");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
