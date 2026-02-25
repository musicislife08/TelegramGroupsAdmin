using TelegramGroupsAdmin.Configuration.Repositories;

namespace TelegramGroupsAdmin.Services;

/// <summary>
/// DelegatingHandler that dynamically loads API keys from database and adds appropriate headers to HTTP requests
/// </summary>
public class ApiKeyDelegatingHandler : DelegatingHandler
{
    private readonly IServiceProvider _serviceProvider;
    private readonly string _serviceName;
    private readonly string _headerName;
    private readonly string? _headerValueFormat;

    public ApiKeyDelegatingHandler(
        IServiceProvider serviceProvider,
        string serviceName,
        string headerName,
        string? headerValueFormat = null)
    {
        _serviceProvider = serviceProvider;
        _serviceName = serviceName;
        _headerName = headerName;
        _headerValueFormat = headerValueFormat;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        string? apiKey = null;

        using var scope = _serviceProvider.CreateScope();
        var configRepo = scope.ServiceProvider.GetRequiredService<ISystemConfigRepository>();
        var apiKeys = await configRepo.GetApiKeysAsync(cancellationToken);

        if (apiKeys != null)
        {
            apiKey = _serviceName switch
            {
                "VirusTotal" => apiKeys.VirusTotal,
                "SendGrid" => apiKeys.SendGrid,
                _ => null
            };
        }

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            var headerValue = string.IsNullOrWhiteSpace(_headerValueFormat)
                ? apiKey
                : string.Format(_headerValueFormat, apiKey);

            request.Headers.TryAddWithoutValidation(_headerName, headerValue);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
