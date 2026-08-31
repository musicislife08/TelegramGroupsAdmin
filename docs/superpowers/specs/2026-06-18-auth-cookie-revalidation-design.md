# Auth Cookie Revalidation + WebUserIdentity Cascade Unification — Design

**Date:** 2026-06-18
**Issues:** #518 (auth cookies never revalidated against the DB), #464 (migrate pages off `AuthenticationStateProvider` to the `WebUserIdentity` cascade)
**Branch:** `feat/auth-cookie-revalidation`

## Problem

Authentication cookies are issued as 30-day persistent, sliding cookies and are
never revalidated against the database after sign-in. `UserRecord.SecurityStamp`
is rotated on sensitive changes (password change/reset, TOTP enable/disable/reset)
but nothing ever reads it back. There is no `OnValidatePrincipal` /
`ISecurityStampValidator` wired into the cookie options, so a session's identity
and permissions are frozen at login time for up to 30 days.

For this app's threat model (few trusted admins, invite-only) the primary
real-world consequence is **offboarding**: revoking an admin is effectively a soft
suggestion for up to 30 days rather than an immediate action. The security-stamp
infrastructure already exists, so the app currently pays the cost of rotating the
stamp without getting any of the benefit.

Separately (#464), several razor components inject `AuthenticationStateProvider`
and hand-parse claims to compute permission flags or extract the current user id —
the same anti-pattern the cascade was meant to replace. The two issues meet at one
seam: the **permission claim**. This work fixes revalidation *and* unifies all
pages onto the single `WebUserIdentity` cascade.

## Goals

- A web session reflects the database within a bounded window: disable/delete,
  password/TOTP change, and permission change all take effect on live sessions
  instead of lingering for up to 30 days.
- One place reads identity claims (`MainLayout`); every other component reads the
  cascaded `WebUserIdentity`.
- A single, explicit, consistently-applied null-handling contract for the cascade.

## Non-Goals

- Adopting ASP.NET Core Identity / `AddIdentity`. The built-in
  `SecurityStampValidator` is Identity-specific; this app uses hand-rolled cookie
  auth, so we wire a custom handler instead.
- Live, no-relogin permission refresh. A permission change rotates the stamp and
  forces re-login (see Decision 3) — simpler and routes all sensitive changes
  through one invalidation path.
- Distributed-systems patterns (homelab single-instance per project philosophy).

## Decisions (resolved during brainstorming)

1. **Scope:** Do #518 and #464 together.
2. **Mechanism:** Defense in depth — *both* `CookieAuthenticationEvents.OnValidatePrincipal`
   (HTTP edge) *and* a `RevalidatingServerAuthenticationStateProvider` (in-circuit timer).
3. **Permission change:** Rotate `SecurityStamp` on permission change → existing
   sessions fail the stamp check → forced re-login. One unified invalidation path
   for password, TOTP, and permission changes. (Drops #518's optional item 4 "live
   permission refresh".)
4. **Interval:** ~2 minutes for the in-circuit revalidation timer. (Full-page loads
   and SignalR reconnects always revalidate via the HTTP-edge handler.)
5. **Null contract:** Non-null by construction at the boundary (render gate + no
   partial identity), guard-once in statement contexts, justified `!` / `?.` in
   expression/markup contexts, fail-closed at sinks. See "Null-Handling Contract".

## Architecture

### Component 1 — Shared session validator (the single rule)

A scoped service is the brain both revalidation mechanisms call, so the rule lives
in exactly one place and cannot drift between the HTTP and circuit paths.

```
IUserSessionValidator
    Task<bool> IsStillValidAsync(ClaimsPrincipal principal, CancellationToken ct)
```

Logic:
1. Extract `ClaimTypes.NameIdentifier` and the new security-stamp claim.
2. If either claim is null/empty → return **false** (fail closed).
3. Load the user via `IUserRepository.GetByIdAsync(userId, ct)`.
4. Return **false** if the user is null, `!Status.CanLogin` (Disabled/Deleted), or
   `SecurityStamp != stampClaim`. Otherwise **true**.

A DB failure during validation is treated as invalid (reject) — fail closed, never
fail open to a stale session.

### Component 2 — HTTP-edge handler (`OnValidatePrincipal`)

Add `options.Events = new CookieAuthenticationEvents { OnValidatePrincipal = ... }`
to the `AddCookie` options block in `AddCookieAuthentication`
(`ServiceCollectionExtensions.cs:71`). On each fire:
- Resolve `IUserSessionValidator` from `context.HttpContext.RequestServices`.
- If invalid → `context.RejectPrincipal()` and `await context.HttpContext.SignOutAsync(...)`.

No throttle. This handler only fires on full-page loads and SignalR (re)connection,
which is infrequent for a Blazor Server app, and `GetByIdAsync` is a single indexed
primary-key lookup. (The issue's suggested `AuthenticationProperties` throttle is
deliberately omitted for simplicity at this scale.) This path also protects any
non-circuit cookie-authed endpoint.

### Component 3 — In-circuit revalidation (`RevalidatingServerAuthenticationStateProvider`)

Replace the plain `ServerAuthenticationStateProvider` registration at
`ServiceCollectionExtensions.cs:94` with a new subclass:

```
sealed class RevalidatingUserAuthenticationStateProvider : RevalidatingServerAuthenticationStateProvider
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(2);
    protected override Task<bool> ValidateAuthenticationStateAsync(
        AuthenticationState state, CancellationToken ct)
        // create a scope, resolve IUserSessionValidator, call IsStillValidAsync
}
```

Returning false tears down the live circuit → redirect to login. This is the piece
that kills an offboarded admin's *open tab* (the HTTP-edge handler alone would let
an open tab survive until reload/reconnect). The interval constant lives in
`AuthenticationConstants`.

### Component 4 — Security-stamp claim (prerequisite)

The stamp must be in the cookie to compare it. Add it in
`AuthCookieService.CreateClaimsPrincipal` (the single source of truth for cookie
claims, feeding both the live `SignInAsync` and the test-only `GenerateCookieValue`).

The stamp is **not** added to `WebUserIdentity` — pages and the cascade must not see
it. Instead the cookie-service methods (`SignInAsync`, `GenerateCookieValue`,
`CreateClaimsPrincipal`) take the security stamp as an explicit parameter. The login
call site (`AuthService.LoginAsync`, which already holds the `UserRecord`) passes
`user.SecurityStamp`. Tests pass any value.

A new claim type (e.g. `CustomClaimTypes.SecurityStamp`) is added.

### Component 5 — Close the permission-change gap

`UserManagementService.UpdatePermissionLevelAsync` (`UserManagementService.cs:17`)
currently writes the new permission level but does **not** rotate the stamp. Add a
call to `IUserRepository.UpdateSecurityStampAsync` after the permission write, in the
same flow. Password change/reset (`AuthService`) and TOTP enable/disable/reset
(`TotpService`) already rotate the stamp, so this is the only gap. Disable/delete
need no stamp rotation — the validator's `Status.CanLogin` check catches them
directly.

### Component 6 — #464 cascade unification

Migrate the remaining components off `@inject AuthenticationStateProvider` to the
cascaded `[CascadingParameter] WebUserIdentity? WebUser`. `MainLayout` remains the
*only* claim-parser (the cascade source).

| File | Current | Action |
|---|---|---|
| `NavMenu.razor` | reads PermissionLevel claim | → `WebUser?.IsGlobalAdminOrHigher` |
| `TagManagement.razor` | `IsInRole("Admin")\|\|IsInRole("Owner")` | → cascade flag — **verify exact tier semantics match** before swapping (IsInRole may not equal `IsGlobalAdminOrHigher`) |
| `BackupPassphraseRotationDialog.razor` | NameIdentifier | → `WebUser?.Id` (guarded) |
| `InviteManagementDialog.razor` | NameIdentifier | → `WebUser?.Id` (guarded) |
| `StopWords.razor` | NameIdentifier (`_currentUserId` **unused**) | → **delete dead code** |
| `TrainingData.razor` | NameIdentifier (`_currentUserId` **unused**) | → **delete dead code** |

After each swap, drop now-unused usings/injects: `@inject AuthenticationStateProvider`,
`@using Microsoft.AspNetCore.Components.Authorization`, `@using System.Security.Claims`,
`@using TelegramGroupsAdmin.Auth` (verify no other reference remains before deleting).

## Null-Handling Contract

The cascade can legitimately be null in three situations: (1) the first render of a
child page before `MainLayout`'s async `OnInitializedAsync` populates `_webUser`;
(2) an authenticated principal missing the `NameIdentifier` claim (the source
currently force-unwraps `_userId!`); (3) a reusable component rendered without a
`MainLayout` ancestor (no cascade supplied). Today the codebase handles this
inconsistently — some sites guard (`Messages.razor:229`, `BackupBrowser.razor:326`),
others force-unwrap (`Messages.razor:418/562/794/808`, `WelcomeSystemConfig.razor:605`,
`MainLayout.razor:117`). The force-unwraps are latent `NullReferenceException`s, and
adding runtime principal re-issuing widens every null window.

The contract, established by this work:

1. **Source (`MainLayout`) — the precondition that makes everything below valid:**
   - **No partial identities.** Remove the `_userId!` force-unwrap. If the principal
     is authenticated but `NameIdentifier` is missing, treat it as not a usable
     session (leave `_webUser` null, do not render authorized content) and log a
     warning.
   - **Render gate.** Do not render `@Body` until auth resolution completes. Net
     effect: when an authorized page renders, an authenticated user's `WebUser` is
     guaranteed non-null. *Implementation note:* gate on "auth resolution done", not
     "is authenticated", so anonymous/login routes still render and the `[Authorize]`
     redirect still fires. Verify login/anonymous pages are not regressed (check
     whether they share `MainLayout`).

2. **Statement-body contexts** (methods in `@code`): guard once
   (`if (WebUser is null) return;`), then use `WebUser` unqualified. C# nullable flow
   analysis narrows it to non-null, so no `!` is needed and there is no compiler
   warning. Preferred wherever the syntax allows it.

3. **Expression / markup contexts** where a guard statement is syntactically
   impossible (attribute bindings, `@()` interpolations, expression-bodied members,
   ternaries, render fragments):
   - Use **`?.` + a safe default** when null should degrade gracefully — e.g.
     `WebUser?.PermissionLevel >= GlobalAdmin` correctly hides an admin affordance
     when null. (`Messages.razor:89` stays as-is.)
   - Use **`WebUser!`** when there is no meaningful default and the gate guarantees
     non-null — it documents the enforced invariant. (`Messages.razor:794` stays `!`.)
   - The render gate does not *eliminate* `!`; it *legitimizes* it. A `WebUser!`
     inside a gated `[Authorize]` page in an un-guardable syntactic position is a true
     statement of a boundary-enforced invariant, not a smell.

4. **Reusable components without the gate guarantee** (`WelcomeSystemConfig`,
   `BackupBrowser`, the migrated dialogs): these can render outside a gated
   `MainLayout`, so the invariant does **not** apply. They must **guard or `?.`**,
   never `!`. `WelcomeSystemConfig.razor:605` `WebUser!.ToActor()` is in a method body
   → convert to a guard.

5. **Sinks fail closed:**
   - The server validator rejects null/empty `NameIdentifier` or stamp claims.
   - Audit-id sinks (`AddedBy` in the StopWords/TrainingData/dialog migrations) are
     protected by the entry guard, so a null actor can never reach the DB — the
     operation aborts instead.

**Per-site audit rule for the migration:** for each `WebUser` use, classify it —
(a) gated page + un-guardable syntax → `!` is OK; (b) gated page + statement body →
guard; (c) reusable component → guard / `?.` always. We delete the `_userId!` at the
source and the reusable-component force-unwraps; we keep (now-justified) the
page-level `!`s in markup/expression positions.

## Error Handling

- Validator DB failure → treat session as invalid (fail closed).
- Missing/empty identity claims → invalid session.
- Permission-change stamp rotation shares the existing `UpdatePermissionLevelAsync`
  transaction/flow; if the permission write succeeds the stamp rotation must also be
  persisted (single save) so a session can't survive a successful downgrade.

## Testing

- **Unit — `IUserSessionValidator` truth table:** valid; missing user; Disabled;
  Deleted; stamp mismatch; missing NameIdentifier claim; missing stamp claim. Each
  asserts the expected valid/invalid result.
- **Integration — revocation:** sign in (using the `GenerateCookieValue` helper to
  mint a cookie with a chosen stamp), then in the DB rotate the stamp / disable /
  delete / change permission, and assert the next validation rejects the session.
- **Permission change:** assert `UpdatePermissionLevelAsync` rotates the stamp.
- **Null contract:** a smoke test / manual verification that an authorized page does
  not NRE on first render and that the render gate does not regress the login page.

## Affected Files

- `TelegramGroupsAdmin/ServiceCollectionExtensions.cs` — add `OnValidatePrincipal`;
  swap the `AuthenticationStateProvider` registration to the revalidating subclass.
- `TelegramGroupsAdmin/Constants/AuthenticationConstants.cs` — revalidation-interval
  constant.
- `TelegramGroupsAdmin/Services/Auth/AuthCookieService.cs` — add stamp claim; thread
  the stamp parameter through `SignInAsync` / `GenerateCookieValue` /
  `CreateClaimsPrincipal`.
- `TelegramGroupsAdmin/Auth/CustomClaimTypes.cs` — add `SecurityStamp` claim type.
- `TelegramGroupsAdmin/Services/AuthService.cs` — pass `user.SecurityStamp` at the
  `LoginAsync` sign-in call site.
- `TelegramGroupsAdmin/Services/UserManagementService.cs` — rotate stamp in
  `UpdatePermissionLevelAsync`.
- New: `IUserSessionValidator` + implementation; `RevalidatingUserAuthenticationStateProvider`.
- `TelegramGroupsAdmin/Components/Layout/MainLayout.razor` — no partial identity +
  render gate.
- `#464` migration: `NavMenu.razor`, `TagManagement.razor`,
  `BackupPassphraseRotationDialog.razor`, `InviteManagementDialog.razor`,
  `StopWords.razor`, `TrainingData.razor`.
- Null-contract cleanup: `WelcomeSystemConfig.razor` (guard `ToActor()`); confirm
  `Messages.razor` page-level `!`s remain valid under the gate.
- `.gitignore` — triage-artifact ignores (already committed on this branch).

## Acceptance Criteria

- [ ] Disabling/deleting a web user ends active sessions within the validation
      interval (open tab) and immediately on next reload/reconnect.
- [ ] Rotating the security stamp (password change/reset, TOTP enable/disable/reset,
      permission change) invalidates other sessions.
- [ ] Permission change forces re-login with fresh claims; no stale elevated access.
- [ ] Security-stamp claim is added at sign-in and compared on validation; null
      claims fail closed.
- [ ] All 6 #464 files read the `WebUserIdentity` cascade; no remaining
      `AuthenticationStateProvider` injection outside `MainLayout`.
- [ ] Null contract applied per the per-site audit rule; no force-unwrap that isn't
      backed by the render-gate invariant; reusable components guard.
- [ ] No NRE on first render of an authorized page; login/anonymous pages not
      regressed by the render gate.
