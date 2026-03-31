using Microsoft.Extensions.Options;
using TelegramGroupsAdmin.Configuration;

namespace TelegramGroupsAdmin.Services;

public class InternalApiClient(IHttpClientFactory factory, IOptions<AppOptions> appOptions)
{
    public HttpClient CreateClient()
    {
        var client = factory.CreateClient("Internal");
        client.BaseAddress = new Uri(appOptions.Value.BaseUrl);
        return client;
    }
}
