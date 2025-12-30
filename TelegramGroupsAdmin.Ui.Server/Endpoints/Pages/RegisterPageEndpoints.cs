using TelegramGroupsAdmin.Ui.Api;
using TelegramGroupsAdmin.Ui.Models;
using TelegramGroupsAdmin.Ui.Server.Services;
using TelegramGroupsAdmin.Ui.Server.Services.Auth;

namespace TelegramGroupsAdmin.Ui.Server.Endpoints.Pages;

public static class RegisterPageEndpoints
{
    public static IEndpointRouteBuilder MapRegisterPageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Routes.Pages.Register, async (
            IAuthService authService,
            IFeatureAvailabilityService featureService) =>
        {
            var isFirstRun = await authService.IsFirstRunAsync();
            var isEmailVerificationEnabled = await featureService.IsEmailVerificationEnabledAsync();

            return Results.Ok(RegisterPageResponse.Ok(isFirstRun, isEmailVerificationEnabled));
        }).AllowAnonymous();

        return endpoints;
    }
}
