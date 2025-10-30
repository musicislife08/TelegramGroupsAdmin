using Microsoft.Extensions.Configuration;
using TelegramGroupsAdmin.Configuration.Repositories;

namespace TelegramGroupsAdmin.Services;

/// <summary>
/// DelegatingHandler that dynamically loads API keys from database (with env var fallback)
/// and adds appropriate headers to HTTP requests
/// </summary>
public class ApiKeyDelegatingHandler : DelegatingHandler
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly string _serviceName;
    private readonly string _headerName;

    public ApiKeyDelegatingHandler(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        string serviceName,
        string headerName)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _serviceName = serviceName;
        _headerName = headerName;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Try to get API key from database first
        string? apiKey = null;

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var configRepo = scope.ServiceProvider.GetRequiredService<IFileScanningConfigRepository>();
            var apiKeys = await configRepo.GetApiKeysAsync(cancellationToken);

            if (apiKeys != null)
            {
                apiKey = _serviceName switch
                {
                    "VirusTotal" => apiKeys.VirusTotal,
                    _ => null
                };
            }
        }
        catch
        {
            // Database might not be available during startup - fall through to env var
        }

        // Fallback to environment variable if database key not found
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            var envVarName = $"{_serviceName}:ApiKey";
            apiKey = _configuration[envVarName];
        }

        // Add API key header if found
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.TryAddWithoutValidation(_headerName, apiKey);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
