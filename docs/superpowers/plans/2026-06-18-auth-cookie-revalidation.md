# Auth Cookie Revalidation + WebUserIdentity Cascade Unification — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make web sessions reflect the database within a bounded window (revoke/disable/downgrade take effect on live sessions), and unify all components onto the single `WebUserIdentity` cascade with one explicit null-handling contract.

**Architecture:** A single scoped `IUserSessionValidator` encodes the revocation rule (user exists + `Status == Active` + security-stamp matches). Two mechanisms call it: a `CookieAuthenticationEvents.OnValidatePrincipal` handler at the HTTP edge, and a `RevalidatingServerAuthenticationStateProvider` subclass for the in-circuit (~2 min) timer. The security stamp is added as a cookie claim at sign-in. Permission changes rotate the stamp (forced re-login). Then six components migrate off `AuthenticationStateProvider` to the cascaded `WebUserIdentity`.

**Tech Stack:** .NET 10, Blazor Server, MudBlazor 9, EF Core 10 / PostgreSQL, NUnit + NSubstitute (unit), NUnit + Testcontainers Postgres (integration).

**Reference spec:** `docs/superpowers/specs/2026-06-18-auth-cookie-revalidation-design.md`

**Conventions for every commit in this plan:**
- Conventional commit messages, ending with:
  `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`
- Build check command: `dotnet build TelegramGroupsAdmin.sln`
- Unit test run: `dotnet test TelegramGroupsAdmin.UnitTests/TelegramGroupsAdmin.UnitTests.csproj`
- Integration test run: `dotnet test TelegramGroupsAdmin.IntegrationTests/TelegramGroupsAdmin.IntegrationTests.csproj` (requires Docker)

---

## File Structure

**New files:**
- `TelegramGroupsAdmin/Services/Auth/IUserSessionValidator.cs` — the validation contract.
- `TelegramGroupsAdmin/Services/Auth/UserSessionValidator.cs` — implementation.
- `TelegramGroupsAdmin/Auth/RevalidatingUserAuthenticationStateProvider.cs` — Blazor in-circuit revalidation.
- `TelegramGroupsAdmin.UnitTests/Services/Auth/UserSessionValidatorTests.cs` — validator truth table.
- `TelegramGroupsAdmin.IntegrationTests/Services/Auth/SessionRevocationTests.cs` — end-to-end revocation.

**Modified files:**
- `TelegramGroupsAdmin/Auth/CustomClaimTypes.cs` — add `SecurityStamp`.
- `TelegramGroupsAdmin/Constants/AuthenticationConstants.cs` — add `RevalidationInterval`.
- `TelegramGroupsAdmin/Services/AuthResult.cs` — add `SecurityStamp`.
- `TelegramGroupsAdmin/Services/AuthService.cs` — populate stamp on success results.
- `TelegramGroupsAdmin/Services/Auth/IAuthCookieService.cs` + `AuthCookieService.cs` — thread stamp into claims.
- `TelegramGroupsAdmin/Endpoints/AuthEndpoints.cs` — pass stamp at sign-in (×3).
- `TelegramGroupsAdmin/ServiceCollectionExtensions.cs` — register validator, wire `OnValidatePrincipal`, swap auth state provider.
- `TelegramGroupsAdmin/Services/UserManagementService.cs` — rotate stamp on permission change.
- `TelegramGroupsAdmin/Components/Layout/MainLayout.razor` — no partial identity + render gate.
- `TelegramGroupsAdmin/Components/Layout/NavMenu.razor`, `Components/Shared/Settings/TagManagement.razor`, `Components/Shared/BackupPassphraseRotationDialog.razor`, `Components/Shared/InviteManagementDialog.razor`, `Components/Shared/ContentDetection/StopWords.razor`, `Components/Shared/ContentDetection/TrainingData.razor` — cascade migration.
- `TelegramGroupsAdmin/Components/Shared/WelcomeSystemConfig.razor`, `Components/Shared/ContentDetection/CriticalChecks.razor` — guard `WebUser!.ToActor()`.
- `TelegramGroupsAdmin.UnitTests/Services/Auth/AuthCookieServiceTests.cs` — stamp claim assertions + signature updates.
- `TelegramGroupsAdmin.E2ETests/Fixtures/SharedAuthenticatedTestBase.cs`, `Fixtures/AuthenticatedTestBase.cs` — pass real stamp to `GenerateCookieValue`.

---

## Task 1: Add claim type + revalidation interval constants

**Files:**
- Modify: `TelegramGroupsAdmin/Auth/CustomClaimTypes.cs`
- Modify: `TelegramGroupsAdmin/Constants/AuthenticationConstants.cs`

- [ ] **Step 1: Add the security-stamp claim type**

In `CustomClaimTypes.cs`, add inside the class after the `PermissionLevel` constant:

```csharp
    /// <summary>
    /// Security stamp claim. Compared against the DB on every session revalidation;
    /// a mismatch (stamp rotated by password/TOTP/permission change) invalidates the session.
    /// </summary>
    public const string SecurityStamp = "SecurityStamp";
```

- [ ] **Step 2: Add the revalidation interval constant**

In `AuthenticationConstants.cs`, add after `CookieExpiration`:

```csharp
    /// <summary>
    /// How often an active Blazor circuit revalidates its session against the DB.
    /// Full-page loads and SignalR reconnects revalidate via the HTTP-edge handler regardless.
    /// </summary>
    public static readonly TimeSpan RevalidationInterval = TimeSpan.FromMinutes(2);
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build TelegramGroupsAdmin/TelegramGroupsAdmin.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add TelegramGroupsAdmin/Auth/CustomClaimTypes.cs TelegramGroupsAdmin/Constants/AuthenticationConstants.cs
git commit -F- <<'EOF'
feat(auth): add SecurityStamp claim type and RevalidationInterval constant

Refs #518

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
```

---

## Task 2: Thread the security stamp into the cookie claims

This is the prerequisite for any validation: the stamp must be *in* the cookie. We thread it from the loaded `UserRecord` → `AuthResult` → endpoint → `IAuthCookieService` → claims. The stamp is deliberately **not** added to `WebUserIdentity` (pages/cascade must not see it).

**Files:**
- Modify: `TelegramGroupsAdmin/Services/AuthResult.cs`
- Modify: `TelegramGroupsAdmin/Services/AuthService.cs:162,168,174,192,395`
- Modify: `TelegramGroupsAdmin/Services/Auth/IAuthCookieService.cs`
- Modify: `TelegramGroupsAdmin/Services/Auth/AuthCookieService.cs`
- Modify: `TelegramGroupsAdmin/Endpoints/AuthEndpoints.cs:72,170,219`
- Test: `TelegramGroupsAdmin.UnitTests/Services/Auth/AuthCookieServiceTests.cs`

- [ ] **Step 1: Write a failing test for the stamp claim**

In `AuthCookieServiceTests.cs`, the existing tests call `_service.GenerateCookieValue(TestIdentity())` and `_service.SignInAsync(...)`. After this task those signatures take a stamp argument. Add a new test that asserts the principal carries the stamp claim. Because `CreateClaimsPrincipal` is private, assert via the `TicketDataFormat.Protect` capture (the existing test file already mocks `_mockTicketDataFormat`). Add:

```csharp
[Test]
public void GenerateCookieValue_IncludesSecurityStampClaim()
{
    // Arrange
    const string stamp = "stamp-abc-123";
    AuthenticationTicket? captured = null;
    _mockTicketDataFormat.Protect(Arg.Do<AuthenticationTicket>(t => captured = t)).Returns("protected");

    // Act
    _service.GenerateCookieValue(TestIdentity(), stamp);

    // Assert
    Assert.That(captured, Is.Not.Null);
    var stampClaim = captured!.Principal.FindFirst(CustomClaimTypes.SecurityStamp);
    Assert.That(stampClaim?.Value, Is.EqualTo(stamp));
}
```

- [ ] **Step 2: Run the test to verify it fails (compile error)**

Run: `dotnet test TelegramGroupsAdmin.UnitTests/TelegramGroupsAdmin.UnitTests.csproj --filter "FullyQualifiedName~AuthCookieServiceTests"`
Expected: FAIL — does not compile (`GenerateCookieValue` takes 1 arg; `CustomClaimTypes.SecurityStamp` may already exist from Task 1).

- [ ] **Step 3: Add `SecurityStamp` to `AuthResult` (defaulted, so failure paths are unchanged)**

In `AuthResult.cs`, add a trailing defaulted parameter:

```csharp
public record AuthResult(
    bool Success,
    string? UserId,
    string? Email,
    PermissionLevel? PermissionLevel,
    bool TotpEnabled,
    bool RequiresTotp,
    string? ErrorMessage,
    string? SecurityStamp = null
);
```

- [ ] **Step 4: Populate the stamp on the 5 success-path results in `AuthService.cs`**

Lines 162, 168, 174 are in `LoginAsync` where the loaded record is `user`; lines 192 and 395 use `dbUser`. Append the stamp argument:

- Line 162: `return new AuthResult(true, user.WebUser.Id, user.WebUser.Email, user.WebUser.PermissionLevel, true, false, null, user.SecurityStamp);`
- Line 168: `return new AuthResult(true, user.WebUser.Id, user.WebUser.Email, user.WebUser.PermissionLevel, true, true, null, user.SecurityStamp);`
- Line 174: `return new AuthResult(true, user.WebUser.Id, user.WebUser.Email, user.WebUser.PermissionLevel, false, false, null, user.SecurityStamp);`
- Line 192: `return new AuthResult(true, dbUser.WebUser.Id, dbUser.WebUser.Email, dbUser.WebUser.PermissionLevel, true, false, null, dbUser.SecurityStamp);`
- Line 395: `return new AuthResult(true, dbUser.WebUser.Id, dbUser.WebUser.Email, dbUser.WebUser.PermissionLevel, true, false, null, dbUser.SecurityStamp);`

(Leave all `new AuthResult(false, ...)` failure constructions unchanged — `SecurityStamp` defaults to null.)

- [ ] **Step 5: Update the `IAuthCookieService` signatures**

In `IAuthCookieService.cs`, change the two methods that build claims:

```csharp
    Task SignInAsync(HttpContext context, WebUserIdentity user, string securityStamp);
    string GenerateCookieValue(WebUserIdentity user, string securityStamp);
```

- [ ] **Step 6: Thread the stamp through `AuthCookieService.cs`**

Update the three methods so the stamp flows into the claims:

```csharp
    public async Task SignInAsync(HttpContext context, WebUserIdentity user, string securityStamp)
    {
        var principal = CreateClaimsPrincipal(user, securityStamp);

        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.Add(AuthenticationConstants.CookieExpiration)
            });
    }
```

```csharp
    public string GenerateCookieValue(WebUserIdentity user, string securityStamp)
    {
        var options = _cookieOptions.Get(CookieAuthenticationDefaults.AuthenticationScheme);

        var principal = CreateClaimsPrincipal(user, securityStamp);

        var ticket = new AuthenticationTicket(
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.Add(AuthenticationConstants.CookieExpiration),
                IssuedUtc = DateTimeOffset.UtcNow
            },
            CookieAuthenticationDefaults.AuthenticationScheme);

        return options.TicketDataFormat.Protect(ticket);
    }
```

```csharp
    private static ClaimsPrincipal CreateClaimsPrincipal(WebUserIdentity user, string securityStamp)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Email, user.Email ?? ""),
            new(ClaimTypes.Role, user.PermissionLevel.GetDisplayName()),
            new(CustomClaimTypes.PermissionLevel, ((int)user.PermissionLevel).ToString()),
            new(CustomClaimTypes.SecurityStamp, securityStamp)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme,
            nameType: ClaimTypes.Email, roleType: ClaimTypes.Role);
        return new ClaimsPrincipal(identity);
    }
```

- [ ] **Step 7: Pass the stamp at the 3 endpoint sign-in sites**

In `AuthEndpoints.cs`, update lines 72, 170, 219 to pass `result.SecurityStamp!` (non-null on success):

```csharp
await authCookieService.SignInAsync(httpContext, new WebUserIdentity(result.UserId!, result.Email!, result.PermissionLevel!.Value), result.SecurityStamp!);
```

- [ ] **Step 8: Fix the two existing `AuthCookieServiceTests` call sites**

The existing tests call `GenerateCookieValue(TestIdentity())` and `SignInAsync(httpContext, TestIdentity())`. Add the stamp arg to every existing call, e.g.:

```csharp
var result = _service.GenerateCookieValue(TestIdentity(), "test-stamp");
```
```csharp
await _service.SignInAsync(httpContext, TestIdentity(), "test-stamp");
```

(Search the file for `GenerateCookieValue(` and `SignInAsync(` and update each.)

- [ ] **Step 9: Run the unit tests to verify they pass**

Run: `dotnet test TelegramGroupsAdmin.UnitTests/TelegramGroupsAdmin.UnitTests.csproj --filter "FullyQualifiedName~AuthCookieServiceTests"`
Expected: PASS (including the new `GenerateCookieValue_IncludesSecurityStampClaim`).

- [ ] **Step 10: Update the E2E fixtures to pass the seeded user's real stamp**

⚠️ Once revalidation is wired (Task 4) these cookies will be rejected if the stamp doesn't match the DB. Fix the fixtures now. In `SharedAuthenticatedTestBase.cs:112-114` and `AuthenticatedTestBase.cs:102-104`, the user object is seeded into the DB; pass its `SecurityStamp`:

```csharp
var cookieValue = authCookieService.GenerateCookieValue(
    new WebUserIdentity(user.Id, user.Email, user.PermissionLevel), user.SecurityStamp);
```

If the local `user` variable in a fixture does not expose `SecurityStamp`, read it from the same `UserRecord`/seed used to create the DB row (the seed that wrote the user). The cookie stamp MUST equal the DB row's `SecurityStamp`. Do not invent a literal — that would fail revalidation.

- [ ] **Step 11: Build the whole solution**

Run: `dotnet build TelegramGroupsAdmin.sln`
Expected: Build succeeded (E2E project compiles with the new signature).

- [ ] **Step 12: Commit**

```bash
git add TelegramGroupsAdmin/Services/AuthResult.cs TelegramGroupsAdmin/Services/AuthService.cs TelegramGroupsAdmin/Services/Auth/IAuthCookieService.cs TelegramGroupsAdmin/Services/Auth/AuthCookieService.cs TelegramGroupsAdmin/Endpoints/AuthEndpoints.cs TelegramGroupsAdmin.UnitTests/Services/Auth/AuthCookieServiceTests.cs TelegramGroupsAdmin.E2ETests/Fixtures/SharedAuthenticatedTestBase.cs TelegramGroupsAdmin.E2ETests/Fixtures/AuthenticatedTestBase.cs
git commit -F- <<'EOF'
feat(auth): carry security stamp into the auth cookie claims

Thread SecurityStamp from the loaded UserRecord through AuthResult and
IAuthCookieService into a new SecurityStamp claim, so sessions can be
revalidated against the DB. Test fixtures pass the seeded user's real stamp.

Refs #518

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
```

---

## Task 3: Create the shared session validator

**Files:**
- Create: `TelegramGroupsAdmin/Services/Auth/IUserSessionValidator.cs`
- Create: `TelegramGroupsAdmin/Services/Auth/UserSessionValidator.cs`
- Test: `TelegramGroupsAdmin.UnitTests/Services/Auth/UserSessionValidatorTests.cs`

- [ ] **Step 1: Write the failing truth-table test**

Create `UserSessionValidatorTests.cs`. Use NSubstitute for `IUserRepository` and a `NullLogger`. A small helper builds a `UserRecord` with a given status and stamp, and a `ClaimsPrincipal` with given id + stamp claims.

```csharp
using System.Security.Claims;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TelegramGroupsAdmin.Auth;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Repositories;
using TelegramGroupsAdmin.Services.Auth;

namespace TelegramGroupsAdmin.UnitTests.Services.Auth;

[TestFixture]
public class UserSessionValidatorTests
{
    private IUserRepository _userRepository = null!;
    private UserSessionValidator _validator = null!;

    private const string UserId = "user-1";
    private const string Stamp = "stamp-1";

    [SetUp]
    public void SetUp()
    {
        _userRepository = Substitute.For<IUserRepository>();
        _validator = new UserSessionValidator(_userRepository, NullLogger<UserSessionValidator>.Instance);
    }

    private static ClaimsPrincipal Principal(string? userId, string? stamp)
    {
        var claims = new List<Claim>();
        if (userId is not null) claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
        if (stamp is not null) claims.Add(new Claim(CustomClaimTypes.SecurityStamp, stamp));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static UserRecord UserWith(UserStatus status, string stamp) => new(
        WebUser: new WebUserIdentity(UserId, "u@example.com", PermissionLevel.Admin),
        NormalizedEmail: "U@EXAMPLE.COM",
        PasswordHash: "x",
        SecurityStamp: stamp,
        InvitedBy: null,
        IsActive: status == UserStatus.Active,
        TotpSecret: null,
        TotpEnabled: false,
        TotpSetupStartedAt: null,
        CreatedAt: DateTimeOffset.UnixEpoch,
        LastLoginAt: null,
        Status: status,
        ModifiedBy: null,
        ModifiedAt: null,
        EmailVerified: true,
        EmailVerificationToken: null,
        EmailVerificationTokenExpiresAt: null,
        PasswordResetToken: null,
        PasswordResetTokenExpiresAt: null,
        FailedLoginAttempts: 0,
        LockedUntil: null);

    [Test]
    public async Task ValidUser_ReturnsTrue()
    {
        _userRepository.GetByIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(UserWith(UserStatus.Active, Stamp));
        Assert.That(await _validator.IsStillValidAsync(Principal(UserId, Stamp)), Is.True);
    }

    [Test]
    public async Task MissingUserIdClaim_ReturnsFalse()
        => Assert.That(await _validator.IsStillValidAsync(Principal(null, Stamp)), Is.False);

    [Test]
    public async Task MissingStampClaim_ReturnsFalse()
        => Assert.That(await _validator.IsStillValidAsync(Principal(UserId, null)), Is.False);

    [Test]
    public async Task UserNotFound_ReturnsFalse()
    {
        _userRepository.GetByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((UserRecord?)null);
        Assert.That(await _validator.IsStillValidAsync(Principal(UserId, Stamp)), Is.False);
    }

    [TestCase(UserStatus.Disabled)]
    [TestCase(UserStatus.Deleted)]
    [TestCase(UserStatus.Pending)]
    public async Task NonActiveStatus_ReturnsFalse(UserStatus status)
    {
        _userRepository.GetByIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(UserWith(status, Stamp));
        Assert.That(await _validator.IsStillValidAsync(Principal(UserId, Stamp)), Is.False);
    }

    [Test]
    public async Task StampMismatch_ReturnsFalse()
    {
        _userRepository.GetByIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(UserWith(UserStatus.Active, "different-stamp"));
        Assert.That(await _validator.IsStillValidAsync(Principal(UserId, Stamp)), Is.False);
    }

    [Test]
    public async Task RepositoryThrows_FailsClosed()
    {
        _userRepository.GetByIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns<UserRecord?>(_ => throw new InvalidOperationException("db down"));
        Assert.That(await _validator.IsStillValidAsync(Principal(UserId, Stamp)), Is.False);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails (compile error)**

Run: `dotnet test TelegramGroupsAdmin.UnitTests/TelegramGroupsAdmin.UnitTests.csproj --filter "FullyQualifiedName~UserSessionValidatorTests"`
Expected: FAIL — `IUserSessionValidator` / `UserSessionValidator` do not exist.

- [ ] **Step 3: Create the interface**

`IUserSessionValidator.cs`:

```csharp
using System.Security.Claims;

namespace TelegramGroupsAdmin.Services.Auth;

/// <summary>
/// Single source of truth for whether an authenticated principal still corresponds to a
/// valid, active DB user with a matching security stamp. Called by both the cookie
/// OnValidatePrincipal handler (HTTP edge) and the in-circuit revalidating provider.
/// </summary>
public interface IUserSessionValidator
{
    Task<bool> IsStillValidAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Create the implementation**

`UserSessionValidator.cs`:

```csharp
using System.Security.Claims;
using TelegramGroupsAdmin.Auth;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Repositories;

namespace TelegramGroupsAdmin.Services.Auth;

public sealed class UserSessionValidator(
    IUserRepository userRepository,
    ILogger<UserSessionValidator> logger) : IUserSessionValidator
{
    public async Task<bool> IsStillValidAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var stamp = principal.FindFirst(CustomClaimTypes.SecurityStamp)?.Value;

        // Fail closed: a principal without both an id and a stamp cannot be validated.
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(stamp))
            return false;

        try
        {
            var user = await userRepository.GetByIdAsync(userId, cancellationToken);
            if (user is null)
                return false;

            // Offboarding intent: only Active sessions survive. Disabled/Deleted/Pending are rejected.
            // (Deliberately NOT UserRecord.CanLogin, which also folds in transient lockout/email-verify.)
            if (user.Status != UserStatus.Active)
                return false;

            return string.Equals(user.SecurityStamp, stamp, StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            // Fail closed on any DB/transient error rather than allowing a stale session.
            logger.LogWarning(ex, "Session validation failed for user {UserId}; rejecting session", userId);
            return false;
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test TelegramGroupsAdmin.UnitTests/TelegramGroupsAdmin.UnitTests.csproj --filter "FullyQualifiedName~UserSessionValidatorTests"`
Expected: PASS (all truth-table cases).

- [ ] **Step 6: Commit**

```bash
git add TelegramGroupsAdmin/Services/Auth/IUserSessionValidator.cs TelegramGroupsAdmin/Services/Auth/UserSessionValidator.cs TelegramGroupsAdmin.UnitTests/Services/Auth/UserSessionValidatorTests.cs
git commit -F- <<'EOF'
feat(auth): add IUserSessionValidator (status + security-stamp check, fail-closed)

Refs #518

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
```

---

## Task 4: Wire OnValidatePrincipal + register the validator

**Files:**
- Modify: `TelegramGroupsAdmin/ServiceCollectionExtensions.cs:70-97`

- [ ] **Step 1: Register the validator (scoped)**

In `AddCookieAuthentication`, after the `AddScoped<IAuthCookieService, AuthCookieService>()` registration (around line 97), add:

```csharp
            services.AddScoped<TelegramGroupsAdmin.Services.Auth.IUserSessionValidator, TelegramGroupsAdmin.Services.Auth.UserSessionValidator>();
```

- [ ] **Step 2: Add the `OnValidatePrincipal` handler to the cookie options**

Inside the `AddCookie(options => { ... })` block (after `options.AccessDeniedPath = "/access-denied";`, before the closing `})`), add:

```csharp
                    options.Events = new CookieAuthenticationEvents
                    {
                        OnValidatePrincipal = async context =>
                        {
                            if (context.Principal is null)
                                return;

                            var validator = context.HttpContext.RequestServices
                                .GetRequiredService<TelegramGroupsAdmin.Services.Auth.IUserSessionValidator>();

                            if (!await validator.IsStillValidAsync(context.Principal, context.HttpContext.RequestAborted))
                            {
                                context.RejectPrincipal();
                                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                            }
                        }
                    };
```

Ensure the file has `using Microsoft.AspNetCore.Authentication;` (for `SignOutAsync`) and `using Microsoft.Extensions.DependencyInjection;` (for `GetRequiredService`). Add any missing using.

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build TelegramGroupsAdmin/TelegramGroupsAdmin.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add TelegramGroupsAdmin/ServiceCollectionExtensions.cs
git commit -F- <<'EOF'
feat(auth): revalidate cookies against the DB via OnValidatePrincipal

Rejects + signs out sessions whose user is missing/non-active or whose
security stamp no longer matches, on every full-page load / SignalR reconnect.

Refs #518

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
```

---

## Task 5: Add the in-circuit revalidating auth state provider

**Files:**
- Create: `TelegramGroupsAdmin/Auth/RevalidatingUserAuthenticationStateProvider.cs`
- Modify: `TelegramGroupsAdmin/ServiceCollectionExtensions.cs:94`

- [ ] **Step 1: Create the provider**

`RevalidatingUserAuthenticationStateProvider.cs`:

```csharp
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
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
```

- [ ] **Step 2: Swap the registration**

In `ServiceCollectionExtensions.cs`, replace line 94:

```csharp
            services.AddScoped<AuthenticationStateProvider, ServerAuthenticationStateProvider>();
```

with:

```csharp
            services.AddScoped<AuthenticationStateProvider, RevalidatingUserAuthenticationStateProvider>();
```

Add `using TelegramGroupsAdmin.Auth;` if not present.

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build TelegramGroupsAdmin/TelegramGroupsAdmin.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add TelegramGroupsAdmin/Auth/RevalidatingUserAuthenticationStateProvider.cs TelegramGroupsAdmin/ServiceCollectionExtensions.cs
git commit -F- <<'EOF'
feat(auth): revalidate live Blazor circuits every 2 minutes

RevalidatingServerAuthenticationStateProvider subclass calls the shared
session validator on a timer, tearing down circuits for revoked sessions.

Refs #518

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
```

---

## Task 6: Rotate the security stamp on permission change

**Files:**
- Modify: `TelegramGroupsAdmin/Services/UserManagementService.cs:52`
- Test: `TelegramGroupsAdmin.UnitTests/Services/UserManagementServiceTests.cs` (create if absent)

- [ ] **Step 1: Write the failing test**

Create or extend `UserManagementServiceTests.cs`. Mock `IUserRepository` and `IAuditService`; assert `UpdatePermissionLevelAsync` calls `UpdateSecurityStampAsync`.

```csharp
using NSubstitute;
using TelegramGroupsAdmin.Repositories;
using TelegramGroupsAdmin.Services;

namespace TelegramGroupsAdmin.UnitTests.Services;

[TestFixture]
public class UserManagementServiceTests
{
    private IUserRepository _userRepository = null!;
    private IAuditService _auditService = null!;
    private UserManagementService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _userRepository = Substitute.For<IUserRepository>();
        _auditService = Substitute.For<IAuditService>();
        _service = new UserManagementService(_userRepository, _auditService);
    }

    [Test]
    public async Task UpdatePermissionLevelAsync_RotatesSecurityStamp()
    {
        // Act: modifier is Owner (level 2) downgrading a user to Admin (level 0)
        await _service.UpdatePermissionLevelAsync("target-user", permissionLevel: 0, modifiedBy: "owner-user", modifierPermissionLevel: 2);

        // Assert
        await _userRepository.Received(1).UpdatePermissionLevelAsync("target-user", 0, "owner-user", Arg.Any<CancellationToken>());
        await _userRepository.Received(1).UpdateSecurityStampAsync("target-user", Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test TelegramGroupsAdmin.UnitTests/TelegramGroupsAdmin.UnitTests.csproj --filter "FullyQualifiedName~UserManagementServiceTests"`
Expected: FAIL — `UpdateSecurityStampAsync` received 0 calls.

- [ ] **Step 3: Add the stamp rotation**

In `UserManagementService.cs`, immediately after the `UpdatePermissionLevelAsync` repository call (line 52), add:

```csharp
        // Rotate the security stamp so existing sessions for this user are invalidated
        // (the permission change takes effect via forced re-login). Mirrors Reset2FaAsync.
        await userRepository.UpdateSecurityStampAsync(userId, cancellationToken);
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test TelegramGroupsAdmin.UnitTests/TelegramGroupsAdmin.UnitTests.csproj --filter "FullyQualifiedName~UserManagementServiceTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add TelegramGroupsAdmin/Services/UserManagementService.cs TelegramGroupsAdmin.UnitTests/Services/UserManagementServiceTests.cs
git commit -F- <<'EOF'
feat(auth): rotate security stamp on permission change to force re-login

Routes permission changes through the same session-invalidation path as
password/TOTP changes, closing the only remaining stamp-rotation gap.

Refs #518

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
```

---

## Task 7: Integration test — end-to-end session revocation

**Files:**
- Create: `TelegramGroupsAdmin.IntegrationTests/Services/Auth/SessionRevocationTests.cs`

This verifies the validator against a real Postgres user row (the unit tests mock the repo). Follow the `UserRepositoryTests` fixture pattern (`MigrationTestHelper` + `ServiceCollection`).

- [ ] **Step 1: Write the integration test**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using TelegramGroupsAdmin.Auth;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Repositories;
using TelegramGroupsAdmin.Services.Auth;

namespace TelegramGroupsAdmin.IntegrationTests.Services.Auth;

[TestFixture]
[Category("Integration")]
public class SessionRevocationTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;

    [TearDown]
    public void TearDown()
    {
        (_serviceProvider as IDisposable)?.Dispose();
        _testHelper?.Dispose();
    }

    private async Task<(IServiceProvider sp, IUserRepository repo)> SetUpAsync()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromEmptyTemplateAsync();

        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(o => o.UseNpgsql(_testHelper.ConnectionString));
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserSessionValidator, UserSessionValidator>();
        _serviceProvider = services.BuildServiceProvider();

        var scope = _serviceProvider.CreateScope();
        return (_serviceProvider, scope.ServiceProvider.GetRequiredService<IUserRepository>());
    }

    private static ClaimsPrincipal PrincipalFor(string userId, string stamp) =>
        new(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(CustomClaimTypes.SecurityStamp, stamp)
        }, "test"));

    [Test]
    public async Task ValidatorRejectsAfterStampRotation()
    {
        var (sp, repo) = await SetUpAsync();

        // Seed an active, verified user. Use the repository's create method;
        // if the signature differs, adapt to the actual IUserRepository create API.
        var userId = await SeedActiveUserAsync(repo);
        var user = await repo.GetByIdAsync(userId);
        Assert.That(user, Is.Not.Null);

        var validator = sp.CreateScope().ServiceProvider.GetRequiredService<IUserSessionValidator>();
        var principal = PrincipalFor(userId, user!.SecurityStamp);

        Assert.That(await validator.IsStillValidAsync(principal), Is.True, "fresh session should be valid");

        // Rotate the stamp (simulates password/TOTP/permission change).
        await repo.UpdateSecurityStampAsync(userId);

        Assert.That(await validator.IsStillValidAsync(principal), Is.False, "session with old stamp must be rejected");
    }

    [Test]
    public async Task ValidatorRejectsAfterDisable()
    {
        var (sp, repo) = await SetUpAsync();
        var userId = await SeedActiveUserAsync(repo);
        var user = await repo.GetByIdAsync(userId);

        var validator = sp.CreateScope().ServiceProvider.GetRequiredService<IUserSessionValidator>();
        var principal = PrincipalFor(userId, user!.SecurityStamp);
        Assert.That(await validator.IsStillValidAsync(principal), Is.True);

        await repo.UpdateStatusAsync(userId, UserStatus.Disabled, "admin");

        Assert.That(await validator.IsStillValidAsync(principal), Is.False);
    }

    // Helper: seed an Active + EmailVerified user and return its id.
    // Implement using the real IUserRepository creation API (inspect IUserRepository
    // for the create/add method and required fields; set Status=Active, EmailVerified=true).
    private static async Task<string> SeedActiveUserAsync(IUserRepository repo)
    {
        throw new NotImplementedException(
            "Implement using the repository's create API — see IUserRepository / UserRepository " +
            "and existing IntegrationTests seeding for the exact method and required fields.");
    }
}
```

- [ ] **Step 2: Implement `SeedActiveUserAsync` against the real repository API**

Inspect `IUserRepository`/`UserRepository` for the user-creation method and how other integration tests seed users (search `IntegrationTests` for existing user seeding). Replace the `NotImplementedException` body with a real seed that creates an **Active, EmailVerified** user and returns its id. The user must be `Status == Active` so the baseline assertion passes.

- [ ] **Step 3: Run the integration tests (Docker required)**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests/TelegramGroupsAdmin.IntegrationTests.csproj --filter "FullyQualifiedName~SessionRevocationTests"`
Expected: PASS — both `ValidatorRejectsAfterStampRotation` and `ValidatorRejectsAfterDisable`.

- [ ] **Step 4: Commit**

```bash
git add TelegramGroupsAdmin.IntegrationTests/Services/Auth/SessionRevocationTests.cs
git commit -F- <<'EOF'
test(auth): integration coverage for session revocation (stamp + disable)

Refs #518

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
```

---

## Task 8: MainLayout — no partial identity + render gate (null contract source)

**Files:**
- Modify: `TelegramGroupsAdmin/Components/Layout/MainLayout.razor`

- [ ] **Step 1: Add an `_authResolved` flag and guard against a missing id**

Replace `OnInitializedAsync` (lines 102-125) with:

```csharp
    private bool _authResolved;

    protected override async Task OnInitializedAsync()
    {
        var authState = await AuthStateProvider.GetAuthenticationStateAsync();
        var user = authState.User;

        if (user.Identity?.IsAuthenticated ?? false)
        {
            _userId = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(_userId))
            {
                // An authenticated principal with no subject id is anomalous (and possible
                // now that principals are re-issued). Treat as not a usable session rather
                // than constructing a half-valid identity.
                Logger.LogWarning("Authenticated principal is missing the NameIdentifier claim; treating as unauthenticated");
            }
            else
            {
                _isAuthenticated = true;
                _userEmail = user.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;

                var permissionLevel = PermissionLevel.Admin;
                var permissionClaim = user.FindFirst(CustomClaimTypes.PermissionLevel);
                if (permissionClaim != null && int.TryParse(permissionClaim.Value, out var level))
                    permissionLevel = (PermissionLevel)level;
                _webUser = new WebUserIdentity(_userId, _userEmail, permissionLevel);

                await NotificationState.InitializeAsync(_userId);
            }
        }

        _authResolved = true;
    }
```

(Note: `_userId!` force-unwrap is gone; `_webUser` is only built with a non-null id.)

- [ ] **Step 2: Gate the body render on `_authResolved`**

Replace the `<MudMainContent>` block (lines 41-45) with:

```razor
            <MudMainContent>
                <MudContainer MaxWidth="MaxWidth.ExtraExtraLarge" Class="my-4">
                    @if (_authResolved)
                    {
                        @Body
                    }
                    else
                    {
                        <div class="d-flex justify-center my-8">
                            <MudProgressCircular Indeterminate="true" />
                        </div>
                    }
                </MudContainer>
            </MudMainContent>
```

- [ ] **Step 3: Verify login/anonymous pages are not regressed by the gate**

Check which layout the login/register/verify pages use. Search:

Run: `rg -n "@layout" TelegramGroupsAdmin/Components/Pages/Login.razor TelegramGroupsAdmin/Components/Pages/Register.razor`
- If they use a **different** layout (e.g. an empty/auth layout), the gate is irrelevant to them — proceed.
- If they use `MainLayout`, confirm the gate still renders them: `_authResolved` becomes true after auth resolves to *not authenticated*, so `@Body` (the login form) renders normally after a brief spinner. This is acceptable. Note the finding in the commit message.

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build TelegramGroupsAdmin/TelegramGroupsAdmin.csproj`
Expected: Build succeeded.

- [ ] **Step 5: Manually verify no first-render NRE (run the app)**

Run the app (`dotnet run --project TelegramGroupsAdmin`), log in, and load an authorized page that consumes `WebUser` (e.g. `/messages`). Confirm it renders without a `NullReferenceException`. (The render gate guarantees `WebUser` is populated before the page body renders.)

- [ ] **Step 6: Commit**

```bash
git add TelegramGroupsAdmin/Components/Layout/MainLayout.razor
git commit -F- <<'EOF'
fix(auth): MainLayout never builds a partial identity + gates body render

Closes the first-render null window for WebUser on authorized pages and stops
force-unwrapping a possibly-missing NameIdentifier claim. Establishes the
boundary invariant the null contract relies on.

Refs #464

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
```

---

## Task 9: Migrate NavMenu.razor to the cascade

**Files:**
- Modify: `TelegramGroupsAdmin/Components/Layout/NavMenu.razor`

- [ ] **Step 1: Replace injection + claim parsing with the cascade**

Remove these top-of-file lines:

```razor
@using Microsoft.AspNetCore.Components.Authorization
@inject AuthenticationStateProvider AuthStateProvider
```

(Keep `@using TelegramGroupsAdmin.Auth` only if still referenced elsewhere in the file; otherwise remove it too.)

Replace the `@code` block (the `_isAuthenticated`/`_isGlobalAdminOrOwner` fields and `OnInitializedAsync`) with the cascade + computed flags:

```csharp
@code {
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [CascadingParameter] private WebUserIdentity? WebUser { get; set; }

    private bool _isAuthenticated => WebUser is not null;
    private bool _isGlobalAdminOrOwner => WebUser?.IsGlobalAdminOrHigher ?? false;
}
```

(These are expression-bodied properties — markup context — so they use `?.`/null-coalescing per the null contract. `@if (_isGlobalAdminOrOwner)` markup is unchanged.)

- [ ] **Step 2: Add the `WebUserIdentity` using if needed**

Ensure the file can resolve `WebUserIdentity`. Add at top if absent:

```razor
@using TelegramGroupsAdmin.Core.Models
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build TelegramGroupsAdmin/TelegramGroupsAdmin.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add TelegramGroupsAdmin/Components/Layout/NavMenu.razor
git commit -F- <<'EOF'
refactor(auth): NavMenu reads WebUser cascade instead of AuthStateProvider

Refs #464

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
```

---

## Task 10: Migrate TagManagement.razor (IsInRole → cascade)

**Files:**
- Modify: `TelegramGroupsAdmin/Components/Shared/Settings/TagManagement.razor`

⚠️ **Authorization-semantics flag:** the current code is `_canEdit = user.IsInRole("Admin") || user.IsInRole("Owner")`. The Role claim is a *single* role equal to the user's exact permission-level display name, so this grants edit to **exactly Admin and exactly Owner — NOT GlobalAdmin**. That is almost certainly a pre-existing bug, but this refactor must **preserve behavior**, not silently change authorization. Reproduce the exact semantics, and surface the bug for a separate decision (do not fix it here).

- [ ] **Step 1: Confirm the role-string ↔ permission-level mapping**

Run: `rg -n "GetDisplayName" TelegramGroupsAdmin --glob '*.cs' | rg -i "permission"`
Open the `PermissionLevel.GetDisplayName()` implementation and confirm the display strings (e.g. does `Admin` map to `"Admin"`, `Owner` to `"Owner"`). This confirms `IsInRole("Admin")`/`IsInRole("Owner")` correspond to `PermissionLevel.Admin`/`PermissionLevel.Owner` exactly.

- [ ] **Step 2: Replace injection + computation with the cascade (behavior-preserving)**

Remove:

```razor
@using Microsoft.AspNetCore.Components.Authorization
@inject AuthenticationStateProvider AuthenticationStateProvider
```

Replace the `_canEdit` field + `OnInitializedAsync` (lines 94-107) with:

```csharp
@code {
    private List<TagDefinition> _tags = [];
    private bool _loading = true;
    [CascadingParameter] private WebUserIdentity? WebUser { get; set; }

    // Preserves prior IsInRole("Admin")||IsInRole("Owner") semantics EXACTLY:
    // grants edit to Admin and Owner only (NOT GlobalAdmin). See authorization-semantics
    // flag in the plan — likely a pre-existing bug to be decided separately.
    private bool _canEdit => WebUser?.PermissionLevel is PermissionLevel.Admin or PermissionLevel.Owner;

    protected override async Task OnInitializedAsync()
    {
        await LoadTags();
    }
}
```

Ensure `@using TelegramGroupsAdmin.Core.Models` is present for `WebUserIdentity`/`PermissionLevel`.

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build TelegramGroupsAdmin/TelegramGroupsAdmin.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add TelegramGroupsAdmin/Components/Shared/Settings/TagManagement.razor
git commit -F- <<'EOF'
refactor(auth): TagManagement reads WebUser cascade (behavior-preserving)

Reproduces the prior IsInRole("Admin")||IsInRole("Owner") semantics exactly
(Admin or Owner, not GlobalAdmin). The GlobalAdmin exclusion looks like a
pre-existing bug and is flagged for a separate decision, not changed here.

Refs #464

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
```

---

## Task 11: Migrate the user-id consumers (dialogs + content-detection pages)

All four read `ClaimTypes.NameIdentifier` into a `_userId`/`_currentUserId`. Migrate each to the cascade. Per the null contract: these are reusable components (dialogs / shared pages) → **guard, never `!`**. The user-id sinks must not write a null actor.

**Files:**
- Modify: `TelegramGroupsAdmin/Components/Shared/BackupPassphraseRotationDialog.razor`
- Modify: `TelegramGroupsAdmin/Components/Shared/InviteManagementDialog.razor`
- Modify: `TelegramGroupsAdmin/Components/Shared/ContentDetection/StopWords.razor`
- Modify: `TelegramGroupsAdmin/Components/Shared/ContentDetection/TrainingData.razor`

- [ ] **Step 1: BackupPassphraseRotationDialog — cascade + guard**

Remove:

```razor
@using Microsoft.AspNetCore.Components.Authorization
@inject AuthenticationStateProvider AuthStateProvider
```

Add the cascade parameter to `@code` (alongside the existing `[CascadingParameter] IMudDialogInstance MudDialog`):

```csharp
    [CascadingParameter] private WebUserIdentity? WebUser { get; set; }
```

In `StartRotation()`, replace the whole `authState`/`userIdClaim`/`throw`/`_userId = userId` block (lines ~181-199) with a guard:

```csharp
            if (WebUser is null)
            {
                Snackbar.Add("Your session could not be verified. Please sign in again.", Severity.Error);
                _isProcessing = false;
                return;
            }
            _userId = WebUser.Id;
```

Ensure `@using TelegramGroupsAdmin.Core.Models`. The later usage `RotatePassphraseAsync(BackupDirectory, _userId)` (line 266) is unchanged.

- [ ] **Step 2: InviteManagementDialog — cascade + guard**

Remove:

```razor
@using Microsoft.AspNetCore.Components.Authorization
@inject AuthenticationStateProvider AuthStateProvider
```

Add to `@code`:

```csharp
    [CascadingParameter] private WebUserIdentity? WebUser { get; set; }
```

Replace `OnInitializedAsync` (lines 124-135) with:

```csharp
    protected override async Task OnInitializedAsync()
    {
        await LoadInvitesAsync();
    }
```

Change the field `private string? _currentUserId;` usage at line 207. Replace:

```csharp
var success = await InviteService.RevokeInviteAsync(token, _currentUserId!);
```

with a guarded version (in the enclosing method):

```csharp
        if (WebUser is null)
        {
            Snackbar.Add("Your session could not be verified. Please sign in again.", Severity.Error);
            return;
        }
        var success = await InviteService.RevokeInviteAsync(token, WebUser.Id);
```

Remove the now-unused `_currentUserId` field. Ensure `@using TelegramGroupsAdmin.Core.Models`.

- [ ] **Step 3: StopWords — cascade + guard**

Remove:

```razor
@using Microsoft.AspNetCore.Components.Authorization
@inject AuthenticationStateProvider AuthStateProvider
```

Add to `@code`:

```csharp
    [CascadingParameter] private WebUserIdentity? WebUser { get; set; }
```

Replace `OnInitializedAsync` (lines 138-143) with:

```csharp
    protected override async Task OnInitializedAsync()
    {
        await LoadStopWords();
    }
```

Remove the `private string? _currentUserId;` field. In the method that builds the `StopWord` (line ~185-191), add a guard before constructing it and use `WebUser.Id` for `AddedBy`:

```csharp
        if (WebUser is null)
        {
            Snackbar.Add("Your session could not be verified. Please sign in again.", Severity.Error);
            return;
        }
        var stopWord = new TelegramGroupsAdmin.ContentDetection.Models.StopWord(
            Id: 0,
            Word: data.Word.ToLowerInvariant(),
            Enabled: true,
            AddedDate: DateTimeOffset.UtcNow,
            AddedBy: WebUser.Id,
            Notes: data.Notes
        );
```

Ensure `@using TelegramGroupsAdmin.Core.Models`.

- [ ] **Step 4: TrainingData — cascade + guard**

Remove:

```razor
@using Microsoft.AspNetCore.Components.Authorization
@inject AuthenticationStateProvider AuthStateProvider
```

Add to `@code`:

```csharp
    [CascadingParameter] private WebUserIdentity? WebUser { get; set; }
```

Replace `OnInitializedAsync` (lines ~205-209) with:

```csharp
    protected override async Task OnInitializedAsync()
    {
        await LoadData();
    }
```

Remove the `private string? _currentUserId;` field. There are two sink methods (`AddTrainingSample` ~line 303-310, `EditTrainingSample` ~line 346-347) that pass `_currentUserId`. In each, add a guard at the top of the method and pass `WebUser.Id`:

```csharp
        if (WebUser is null)
        {
            Snackbar.Add("Your session could not be verified. Please sign in again.", Severity.Error);
            return;
        }
```

Then change `_currentUserId` → `WebUser.Id` in the `AddManualTrainingSampleAsync(...)` calls. Ensure `@using TelegramGroupsAdmin.Core.Models`.

- [ ] **Step 5: Build to verify all four compile**

Run: `dotnet build TelegramGroupsAdmin/TelegramGroupsAdmin.csproj`
Expected: Build succeeded, with no remaining references to `AuthStateProvider` in these files.

- [ ] **Step 6: Verify no stray AuthStateProvider/claim usings remain**

Run: `rg -n "AuthenticationStateProvider|System.Security.Claims" TelegramGroupsAdmin/Components/Shared/BackupPassphraseRotationDialog.razor TelegramGroupsAdmin/Components/Shared/InviteManagementDialog.razor TelegramGroupsAdmin/Components/Shared/ContentDetection/StopWords.razor TelegramGroupsAdmin/Components/Shared/ContentDetection/TrainingData.razor`
Expected: no output (all removed).

- [ ] **Step 7: Commit**

```bash
git add TelegramGroupsAdmin/Components/Shared/BackupPassphraseRotationDialog.razor TelegramGroupsAdmin/Components/Shared/InviteManagementDialog.razor TelegramGroupsAdmin/Components/Shared/ContentDetection/StopWords.razor TelegramGroupsAdmin/Components/Shared/ContentDetection/TrainingData.razor
git commit -F- <<'EOF'
refactor(auth): migrate user-id consumers to WebUser cascade with guards

Dialogs and content-detection pages read WebUser.Id instead of parsing the
NameIdentifier claim; each sink guards against a null session so no null actor
reaches the DB.

Refs #464

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
```

---

## Task 12: Null-contract cleanup — guard the ToActor() sinks

`WebUser!.ToActor()` appears in two shared components used inside settings pages. Per the null contract, reusable components guard rather than force-unwrap (they could render without the layout gate). Both are in method bodies, so a guard is feasible.

**Files:**
- Modify: `TelegramGroupsAdmin/Components/Shared/WelcomeSystemConfig.razor:605`
- Modify: `TelegramGroupsAdmin/Components/Shared/ContentDetection/CriticalChecks.razor:245`

- [ ] **Step 1: WelcomeSystemConfig — guard SaveConfig**

In `SaveConfig()` (lines 598-623), replace:

```csharp
        var actor = WebUser!.ToActor();
```

with:

```csharp
        if (WebUser is null)
        {
            Snackbar.Add("Your session could not be verified. Please sign in again.", Severity.Error);
            _saving = false;
            return;
        }
        var actor = WebUser.ToActor();
```

- [ ] **Step 2: CriticalChecks — guard the save**

In the method around line 245, replace:

```csharp
        var actor = WebUser!.ToActor();
        await ConfigService.SaveContentDetectionAsync(new ChatIdentity(0, "Global"), _config, actor);
```

with:

```csharp
        if (WebUser is null)
        {
            Snackbar.Add("Your session could not be verified. Please sign in again.", Severity.Error);
            return;
        }
        var actor = WebUser.ToActor();
        await ConfigService.SaveContentDetectionAsync(new ChatIdentity(0, "Global"), _config, actor);
```

(If `Snackbar` is not injected in `CriticalChecks.razor`, check the file — if absent, log via the component's existing error-surfacing mechanism instead; do not introduce a new injection if the file already has an error pattern.)

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build TelegramGroupsAdmin/TelegramGroupsAdmin.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add TelegramGroupsAdmin/Components/Shared/WelcomeSystemConfig.razor TelegramGroupsAdmin/Components/Shared/ContentDetection/CriticalChecks.razor
git commit -F- <<'EOF'
refactor(auth): guard WebUser!.ToActor() in shared config components

Reusable components can render without the layout gate, so they guard instead
of force-unwrapping the WebUser cascade.

Refs #464

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
```

---

## Task 13: Full verification pass

**Files:** none (verification only)

- [ ] **Step 1: Build the whole solution**

Run: `dotnet build TelegramGroupsAdmin.sln`
Expected: Build succeeded, 0 errors.

- [ ] **Step 2: Run all unit tests**

Run: `dotnet test TelegramGroupsAdmin.UnitTests/TelegramGroupsAdmin.UnitTests.csproj`
Expected: PASS.

- [ ] **Step 3: Run integration tests (Docker required)**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests/TelegramGroupsAdmin.IntegrationTests.csproj`
Expected: PASS.

- [ ] **Step 4: Confirm no AuthStateProvider injection remains outside MainLayout**

Run: `rg -n "@inject AuthenticationStateProvider" TelegramGroupsAdmin`
Expected: only `Components/Layout/MainLayout.razor`.

- [ ] **Step 5: Manual smoke test (run the app)**

- Log in → land on an authorized page (no NRE).
- In a second browser/incognito as an Owner, change the first user's permission level → within ~2 minutes the first session's circuit is torn down (redirect to login), or immediately on reload.
- Disable the first user → same outcome.

- [ ] **Step 6: Final review + push**

Run: `git log --oneline develop..HEAD` to review the commit series, then push the branch:

Run: `git push -u origin feat/auth-cookie-revalidation`

---

## Self-Review Notes (author)

- **Spec coverage:** validator (Task 3), OnValidatePrincipal (Task 4), revalidating provider (Task 5), stamp claim (Task 2), permission-change stamp rotation (Task 6), #464 six-file migration (Tasks 9-11), null contract source + sinks (Tasks 8, 11, 12), tests (Tasks 3, 6, 7), `.gitignore` (already committed). All spec sections map to a task.
- **Known adaptation point:** `SeedActiveUserAsync` (Task 7) and the exact fixture `SecurityStamp` source (Task 2 Step 10) depend on the real repository/seed APIs — each step says how to find them. These are integration seams, not placeholders in the design.
- **Behavior-preservation flag:** TagManagement `IsInRole` semantics (Task 10) are reproduced exactly; the likely GlobalAdmin-exclusion bug is flagged for a separate decision.
