using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace TelegramGroupsAdmin.ComponentTests;

/// <summary>
/// Base test context for testing components that use MudBlazor.
/// Sets up required MudBlazor services and JSInterop stubs.
/// </summary>
public abstract class MudBlazorTestContext : BunitContext
{
    protected MudBlazorTestContext()
    {
        // Add MudBlazor services
        Services.AddMudServices(options =>
        {
            options.PopoverOptions.ThrowOnDuplicateProvider = false;
        });

        // Set up JSInterop in loose mode (ignores unmatched JS calls)
        // MudBlazor makes many JS interop calls that we don't need to verify
        JSInterop.Mode = JSRuntimeMode.Loose;
    }
}
