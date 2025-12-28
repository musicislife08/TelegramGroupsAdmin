using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using TelegramGroupsAdmin.Ui;
using TelegramGroupsAdmin.Ui.Api;
using TelegramGroupsAdmin.Ui.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Named HttpClient via IHttpClientFactory - configured once, used everywhere
// Pages inject IHttpClientFactory and call CreateClient(HttpClientNames.Api)
//
// Cookie Authentication: Since BaseAddress = HostEnvironment.BaseAddress (same origin),
// the browser's Fetch API automatically includes cookies on all requests.
// No additional configuration needed. If the API were on a different domain (cross-origin),
// you'd need: request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include)
builder.Services.AddHttpClient(HttpClientNames.Api, client =>
{
    client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);
});

// MudBlazor UI components
builder.Services.AddMudServices();

// Authentication
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<AuthenticationStateProvider, WasmAuthStateProvider>();

// SSE service for real-time updates
builder.Services.AddScoped<SseService>();

// Auth helper for permission checks
builder.Services.AddScoped<BlazorAuthHelper>();

await builder.Build().RunAsync();
