using Bunit;
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
        // Add MudBlazor services with popover provider check disabled for testing
        // This avoids needing to render MudPopoverProvider in the test tree
        Services.AddMudServices(options =>
        {
            options.PopoverOptions.ThrowOnDuplicateProvider = false;
            options.PopoverOptions.CheckForPopoverProvider = false;
        });

        // Set up JSInterop in loose mode (ignores unmatched JS calls)
        // MudBlazor makes many JS interop calls that we don't need to verify
        JSInterop.Mode = JSRuntimeMode.Loose;

        // Set up specific JSInterop stubs for MudBlazor popover components
        JSInterop.SetupVoid("mudPopover.initialize", _ => true);
        JSInterop.SetupVoid("mudPopover.connect", _ => true);
        JSInterop.Setup<int>("mudpopoverHelper.countProviders").SetResult(1);
    }
}
