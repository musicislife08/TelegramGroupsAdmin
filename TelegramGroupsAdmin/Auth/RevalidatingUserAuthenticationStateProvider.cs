using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.Extensions.DependencyInjection;
using TelegramGroupsAdmin.Constants;
using TelegramGroupsAdmin.Services.Auth;

namespace TelegramGroupsAdmin.Auth;

/// <summary>
/// Tears down a live Blazor circuit when its session is no longer valid (user
/// disabled/deleted, or security stamp rotated by password/TOTP/permission change).
/// Complements the cookie OnValidatePrincipal handler, which only runs at the HTTP edge.
/// </summary>
public sealed class RevalidatingUserAuthenticationStateProvider(
    ILoggerFactory loggerFactory,
    IServiceScopeFactory scopeFactory)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => AuthenticationConstants.RevalidationInterval;

    protected override async Task<bool> ValidateAuthenticationStateAsync(
        AuthenticationState authenticationState, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IUserSessionValidator>();
        return await validator.IsStillValidAsync(authenticationState.User, cancellationToken);
    }
}
