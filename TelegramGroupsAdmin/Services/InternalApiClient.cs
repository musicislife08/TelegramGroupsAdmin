namespace TelegramGroupsAdmin.Services;

public class InternalApiClient(IHttpClientFactory factory, IHttpContextAccessor contextAccessor)
{
    public HttpClient CreateClient()
    {
        var client = factory.CreateClient("Internal");
        var httpContext = contextAccessor.HttpContext;
        if (httpContext is null)
        {
            client.BaseAddress = new Uri("http://localhost:5161");
            return client;
        }
        var host = httpContext.Request.Host;
        var hostString = host.Host == "0.0.0.0"
            ? $"localhost:{host.Port}"
            : host.ToString();
        client.BaseAddress = new Uri($"{httpContext.Request.Scheme}://{hostString}");
        return client;
    }
}
