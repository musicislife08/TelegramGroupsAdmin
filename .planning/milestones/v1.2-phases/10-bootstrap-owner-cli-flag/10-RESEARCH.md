# Phase 10: Bootstrap Owner CLI Flag - Research

**Researched:** 2026-03-18
**Domain:** .NET 10 CLI flag pattern, EF Core AnyAsync, System.Text.Json, ASP.NET DI scoped services in early startup
**Confidence:** HIGH

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Password delivery:**
- Single `--bootstrap <path>` flag (no inline args for email/password)
- JSON file at the specified path contains both email and password: `{"email": "...", "password": "..."}`
- File-based delivery designed for K8s Secret volume mounts with `optional: true`
- DB-first idempotency check runs before reading the file — file only needed on first bootstrap
- Orchestrator handles K8s Secret lifecycle (create before deploy, delete after exit 0)
- No env var fallback — file-only delivery

**Idempotency behavior:**
- New `AnyUsersExistAsync()` repo method using EF Core `AnyAsync()` → `SELECT EXISTS(...)` (stops at first row)
- Any users exist in DB → INFO log "already bootstrapped" → exit 0 immediately (no file read)
- Audit log failure after successful user creation is non-fatal — log warning, still exit 0
- Critical operation is account creation; audit log is best-effort

**Exit code semantics:**
- Exit 0: owner created successfully, OR users already exist (idempotent skip)
- Exit 1: any error (file not found, empty file, invalid JSON, missing fields, invalid email, DB failure, user creation failure)
- All error details go to log output — K8s only cares about 0 vs non-zero for restart policy
- Matches existing `--backup`/`--restore` error handling pattern

**Input validation:**
- Basic email validation: non-empty and contains `@` — sanity check, not RFC 5322
- No password strength validation — operator's responsibility, not TGA's concern

**Account creation:**
- EmailVerified=true (headless setup, no inbox — BOOT-04)
- TotpEnabled=true, TotpSecret=null (forces TOTP setup on first browser login — BOOT-05, matches existing `CreateOwnerAccountAsync` pattern)
- Audit log entry with `Actor.FromSystem("bootstrap")` — BOOT-06
- Runs after migrations, before ML training — BOOT-07 (slots between line 190 and 277 in Program.cs)

### Claude's Discretion
- Exact JSON deserialization approach (System.Text.Json, anonymous type, or dedicated record)
- Whether to extract bootstrap logic into a dedicated service or keep inline in Program.cs
- Whether to update `IsFirstRunAsync` in AuthService to reuse `AnyUsersExistAsync()`
- Exact log message wording
- Test approach for verifying bootstrap behavior

### Deferred Ideas (OUT OF SCOPE)
- EXTBOOT-01: Full configuration seeding via CLI (bot token, OpenAI key, etc.) — future milestone
- EXTBOOT-02: `--bootstrap --skip-totp` flag for fully headless environments — future milestone
</user_constraints>

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| BOOT-01 | Operator can create an Owner account headlessly via `--bootstrap <path>` CLI flag, where `<path>` points to a JSON file containing `{"email": "...", "password": "..."}` | Existing `--backup`/`--restore` flag pattern in Program.cs lines 192-273 is the direct template |
| BOOT-02 | Bootstrap exits 0 with INFO log when any user already exists (idempotent for orchestrator retry loops — DB check before file read, so file can be absent on subsequent runs) | New `AnyUsersExistAsync()` on `IUserRepository` using EF Core `AnyAsync()` |
| BOOT-03 | Bootstrap exits 1 with clear error when the JSON file is missing, empty, or has invalid/incomplete content | `File.Exists()` + `JsonSerializer.Deserialize<BootstrapCredentials>()` with null checks; `Environment.Exit(1)` |
| BOOT-04 | Bootstrapped account has `EmailVerified=true` (no inbox for headless setup) | `UserRecord` constructor sets `EmailVerified: true` unconditionally (unlike `CreateOwnerAccountAsync` which checks `IsEmailVerificationEnabledAsync()`) |
| BOOT-05 | Bootstrapped account has `TotpEnabled=true`, `TotpSecret=null` (forces TOTP setup on first browser login, matching existing registration flow) | Mirrors `CreateOwnerAccountAsync` at AuthService.cs line 278 |
| BOOT-06 | Bootstrap writes an audit log entry recording the owner account creation | `IAuditService.LogEventAsync(AuditEventType.UserRegistered, Actor.FromSystem("bootstrap"), ...)` — non-fatal try/catch wrapping |
| BOOT-07 | Bootstrap runs after database migrations and before ML training, following existing CLI flag early-exit pattern | Insert between Program.cs line 190 (`--migrate-only` block) and line 277 (ML training block) |
</phase_requirements>

---

## Summary

Phase 10 is a narrow, mechanical implementation that wires together existing building blocks in a new location (Program.cs) using the established CLI flag pattern. No new libraries are needed. No new architectural concepts are introduced. All the required services (`IUserRepository`, `IPasswordHasher`, `IAuditService`) are already registered in DI before `app.Build()` is called, so they are accessible via `app.Services.CreateScope()` exactly as `--backup` and `--restore` do it.

The only new code surface that does not already exist is: (1) `AnyUsersExistAsync()` on `IUserRepository` and its implementation in `UserRepository`, (2) a small `BootstrapCredentials` record for JSON deserialization, (3) the `--bootstrap` block in Program.cs itself, and optionally (4) a new `OwnerBootstrapped` enum value in `AuditEventType` (both Core and Data versions must stay in sync).

The existing `CreateOwnerAccountAsync` in `AuthService` is the source-of-truth for `UserRecord` field values. The bootstrap block should mirror that logic exactly, with the one difference that `EmailVerified` is always `true` (no conditional on email service availability), because headless bootstrap implies no inbox access.

**Primary recommendation:** Keep bootstrap logic inline in Program.cs. It is ~60 lines, has no reuse case outside startup, and extracting it into a service adds complexity with no benefit given the homelab singleton design philosophy.

---

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| System.Text.Json | .NET 10 inbox | JSON deserialization of bootstrap credentials | Already used throughout; no additional dependency |
| Microsoft.EntityFrameworkCore | 10.0.0 | `AnyAsync()` for the idempotency check | Project standard; already in `UserRepository` |
| Microsoft.Extensions.DependencyInjection | .NET 10 inbox | `app.Services.CreateScope()` to resolve services after `app.Build()` | Same pattern as `--backup`/`--restore` |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| NSubstitute | project version | Mock `IUserRepository`, `IPasswordHasher`, `IAuditService` in unit tests | Unit tests for the bootstrap logic |

**Installation:** No new packages required.

---

## Architecture Patterns

### Existing CLI Flag Pattern (HIGH confidence — source: Program.cs lines 192-273)

All CLI flags in this codebase follow the same structure post-`app.Build()`:

```csharp
// After: var app = builder.Build();
// After: await app.RunDatabaseMigrationsAsync();
// Before: ML training block

if (args.Contains("--bootstrap"))
{
    var bootstrapPath = args.SkipWhile(a => a != "--bootstrap").Skip(1).FirstOrDefault();

    if (bootstrapPath == null)
    {
        app.Logger.LogError("--bootstrap requires a file path argument");
        Environment.Exit(1);
    }

    // --- DB-first idempotency check (before file read) ---
    using var scope = app.Services.CreateScope();
    var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();

    if (await userRepository.AnyUsersExistAsync())
    {
        app.Logger.LogInformation("Bootstrap skipped: users already exist in the database");
        Environment.Exit(0);
    }

    // --- File validation ---
    if (!File.Exists(bootstrapPath))
    {
        app.Logger.LogError("Bootstrap file not found: {Path}", bootstrapPath);
        Environment.Exit(1);
    }

    var json = await File.ReadAllTextAsync(bootstrapPath);
    if (string.IsNullOrWhiteSpace(json))
    {
        app.Logger.LogError("Bootstrap file is empty: {Path}", bootstrapPath);
        Environment.Exit(1);
    }

    BootstrapCredentials? credentials;
    try
    {
        credentials = JsonSerializer.Deserialize<BootstrapCredentials>(json);
    }
    catch (JsonException ex)
    {
        app.Logger.LogError(ex, "Bootstrap file contains invalid JSON: {Path}", bootstrapPath);
        Environment.Exit(1);
    }

    if (credentials == null
        || string.IsNullOrWhiteSpace(credentials.Email)
        || string.IsNullOrWhiteSpace(credentials.Password)
        || !credentials.Email.Contains('@'))
    {
        app.Logger.LogError("Bootstrap file must contain non-empty 'email' (with @) and 'password' fields");
        Environment.Exit(1);
    }

    // --- Account creation ---
    var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
    var auditService = scope.ServiceProvider.GetRequiredService<IAuditService>();

    var userId = Guid.NewGuid().ToString();
    var user = new UserRecord(
        WebUser: new WebUserIdentity(userId, credentials.Email, PermissionLevel.Owner),
        NormalizedEmail: credentials.Email.ToUpperInvariant(),
        PasswordHash: passwordHasher.HashPassword(credentials.Password),
        SecurityStamp: Guid.NewGuid().ToString(),
        InvitedBy: null,
        IsActive: true,
        TotpSecret: null,
        TotpEnabled: true,
        TotpSetupStartedAt: null,
        CreatedAt: DateTimeOffset.UtcNow,
        LastLoginAt: null,
        Status: UserStatus.Active,
        ModifiedBy: null,
        ModifiedAt: null,
        EmailVerified: true,          // Always true for headless bootstrap (BOOT-04)
        EmailVerificationToken: null,
        EmailVerificationTokenExpiresAt: null,
        PasswordResetToken: null,
        PasswordResetTokenExpiresAt: null,
        FailedLoginAttempts: 0,
        LockedUntil: null
    );

    await userRepository.CreateAsync(user);
    app.Logger.LogInformation("Owner account created via bootstrap: {Email}", credentials.Email);

    // --- Audit log (non-fatal) ---
    try
    {
        await auditService.LogEventAsync(
            AuditEventType.UserRegistered,
            actor: Actor.FromSystem("bootstrap"),
            target: Actor.FromWebUser(userId),
            value: "Owner account created via --bootstrap CLI flag");
    }
    catch (Exception auditEx)
    {
        app.Logger.LogWarning(auditEx, "Audit log entry failed after bootstrap (non-fatal)");
    }

    app.Logger.LogInformation("Bootstrap complete. Exiting.");
    Environment.Exit(0);
}
```

### BootstrapCredentials Record

A dedicated record is preferred over an anonymous type because it gives a named type for testing and is self-documenting. Keep it in the main `TelegramGroupsAdmin` project (not Core — it is startup-only logic):

```csharp
// Source: recommended pattern — System.Text.Json JsonPropertyName attribute
internal sealed record BootstrapCredentials(
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("password")] string? Password
);
```

Using `string?` (nullable) on both fields allows the null-check in the validation block to catch `{}` or `{"email": null}` inputs without throwing.

### AnyUsersExistAsync Implementation

Add to `IUserRepository`:

```csharp
/// <summary>
/// Returns true if at least one user exists in the database.
/// Used by --bootstrap CLI flag for DB-first idempotency check (stops at first row).
/// </summary>
Task<bool> AnyUsersExistAsync(CancellationToken cancellationToken = default);
```

Implement in `UserRepository`:

```csharp
public async Task<bool> AnyUsersExistAsync(CancellationToken cancellationToken = default)
{
    await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
    return await context.Users.AnyAsync(cancellationToken);
}
```

EF Core translates `AnyAsync()` to `SELECT EXISTS(SELECT 1 FROM users LIMIT 1)` — stops at first row, no count scan.

### IsFirstRunAsync Alignment (Claude's Discretion)

`AuthService.IsFirstRunAsync()` currently calls `GetUserCountAsync()` and checks `== 0`. After adding `AnyUsersExistAsync()`, consider updating `IsFirstRunAsync()` to `return !await userRepository.AnyUsersExistAsync()`. This eliminates the `COUNT(*)` scan in favor of `EXISTS`. This is a correct optimization but is not required for BOOT-* compliance. Recommend: **yes, update it** — it is a two-line change that makes the API more consistent and removes the more expensive query.

### AuditEventType: Reuse vs. New Value

The current `AuditEventType.UserRegistered = 13` is the correct event to reuse. It accurately describes what happened. Adding a new `OwnerBootstrapped` value would require:
1. Adding to `Core/Models/AuditEventType.cs`
2. Adding to `Data/Models/AuditEventType.cs` (both must stay in sync — they are separate enums)
3. No EF Core migration needed (stored as int, new value auto-resolves)

Recommendation: **reuse `UserRegistered`**, differentiate via the `value` field: `"Owner account created via --bootstrap CLI flag"`. This avoids enum sprawl and keeps the distinction readable in the audit log UI without new enum members.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| JSON file parsing | Custom text parser | `JsonSerializer.Deserialize<BootstrapCredentials>()` | Handles encoding, whitespace, malformed JSON with proper exceptions |
| Password hashing | Direct BCrypt call | `IPasswordHasher.HashPassword()` | Registered service — consistent salt/iteration settings across all user creation paths |
| DB existence check | `GetUserCountAsync() == 0` | `AnyAsync()` → `AnyUsersExistAsync()` | `COUNT(*)` scans the whole table; `EXISTS` stops at first row |
| Scope lifetime | Long-lived service resolution | `app.Services.CreateScope()` + `using` | Ensures scoped services (`IDbContextFactory`) are disposed before `Environment.Exit()` |

---

## Common Pitfalls

### Pitfall 1: Resolving Scoped Services Without a Scope
**What goes wrong:** `app.Services.GetRequiredService<IUserRepository>()` throws `InvalidOperationException` because `IUserRepository` is registered as scoped.
**Why it happens:** `IDbContextFactory<AppDbContext>` is scoped; the repo depends on it.
**How to avoid:** Always use `app.Services.CreateScope()` and resolve from `scope.ServiceProvider`, wrapped in `using var scope = ...` as the backup/restore blocks do.
**Warning signs:** `InvalidOperationException: Cannot resolve scoped service ... from root provider`

### Pitfall 2: Reading the File Before the DB Check
**What goes wrong:** K8s Secret is deleted after first successful bootstrap. On pod reschedule, init container runs again, file is gone → exit 1 instead of exit 0.
**Why it happens:** Wrong order of operations.
**How to avoid:** DB check MUST come before `File.Exists()`. The locked decision enforces this. The code structure in the pattern above puts the scope creation and `AnyUsersExistAsync()` call before all file operations.

### Pitfall 3: Non-nullable Deserialization Masking Missing Fields
**What goes wrong:** `JsonSerializer.Deserialize<BootstrapCredentials>(json)` with `string` (non-nullable) properties succeeds silently when the JSON has `{}` — the property is left as default (empty string or throws).
**Why it happens:** System.Text.Json does not throw on missing non-nullable string properties by default; it leaves them as `null` (with nullable enabled) or default.
**How to avoid:** Use `string?` on the record properties, then explicitly null-check after deserialization. The pattern above does this.

### Pitfall 4: CancellationToken Propagation at Exit
**What goes wrong:** `await userRepository.AnyUsersExistAsync()` (no cancellationToken) is fine here — this is a CLI startup path, not a request. Passing a real `CancellationToken` to `Environment.Exit()` paths is unnecessary but harmless.
**How to avoid:** Use `CancellationToken.None` or omit the parameter at call sites — the `cancellationToken:` named argument rule still applies when a token is passed.

### Pitfall 5: Scope Disposal Before Environment.Exit
**What goes wrong:** If `Environment.Exit(0)` is called inside the `using var scope` block, .NET still calls `Dispose()` during CLR shutdown. Async disposal via `await using` and then `Environment.Exit()` after the block exits cleanly is safer.
**How to avoid:** Resolve the scope, do all async work, then call `Environment.Exit()` AFTER the `using` block closes. Alternatively, call `Environment.Exit()` inside the block — CLR cleanup handles it. Existing `--backup`/`--restore` pattern calls `Environment.Exit()` inside the `using var scope` block — follow that same pattern for consistency.

---

## Code Examples

### Pattern: --restore flag (direct template for --bootstrap)
Source: `TelegramGroupsAdmin/Program.cs` lines 227-273

```csharp
if (args.Contains("--restore"))
{
    var restorePath = args.SkipWhile(a => a != "--restore").Skip(1).FirstOrDefault();
    if (restorePath == null)
    {
        app.Logger.LogError("--restore requires a file path argument");
        Environment.Exit(1);
    }
    if (!File.Exists(restorePath))
    {
        app.Logger.LogError("Backup file not found: {Path}", restorePath);
        Environment.Exit(1);
    }
    // ... more validation ...
    using var scope = app.Services.CreateScope();
    var backupService = scope.ServiceProvider.GetRequiredService<IBackupService>();
    // ... do work ...
    Environment.Exit(0);
}
```

### Pattern: UserRecord construction for Owner account
Source: `TelegramGroupsAdmin/Services/AuthService.cs` lines 269-292

```csharp
var userId = Guid.NewGuid().ToString();
var user = new UserRecord(
    WebUser: new WebUserIdentity(userId, email, PermissionLevel.Owner),
    NormalizedEmail: email.ToUpperInvariant(),
    PasswordHash: passwordHasher.HashPassword(password),
    SecurityStamp: Guid.NewGuid().ToString(),
    InvitedBy: null,
    IsActive: true,
    TotpSecret: null,
    TotpEnabled: true,
    TotpSetupStartedAt: null,
    CreatedAt: DateTimeOffset.UtcNow,
    LastLoginAt: null,
    Status: UserStatus.Active,
    ModifiedBy: null,
    ModifiedAt: null,
    EmailVerified: !await featureAvailability.IsEmailVerificationEnabledAsync(), // <-- bootstrap uses: true
    EmailVerificationToken: null,
    EmailVerificationTokenExpiresAt: null,
    PasswordResetToken: null,
    PasswordResetTokenExpiresAt: null,
    FailedLoginAttempts: 0,
    LockedUntil: null
);
```

### Pattern: Actor.FromSystem for system audit entries
Source: `TelegramGroupsAdmin.Core/Models/Actor.cs` lines 92-121

```csharp
// Actor.FromSystem("bootstrap") produces:
// Actor { Type = System, SystemIdentifier = "bootstrap", DisplayName = "bootstrap" }
// (falls through the switch default arm → uses systemIdentifier as DisplayName)
```

Note: the `Actor.cs` switch statement for `FromSystem()` uses a default arm `_ => systemIdentifier` — so `"bootstrap"` will display as `"bootstrap"` in the audit log UI unless a dedicated display name entry is added. Recommendation: add `"bootstrap" => "CLI Bootstrap"` to the switch to get a cleaner display name.

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `GetUserCountAsync() == 0` for first-run check | `AnyUsersExistAsync()` using `AnyAsync()` | Phase 10 (new) | `EXISTS` is faster than `COUNT(*)` for large tables |

**Deprecated/outdated:**
- None relevant to this phase.

---

## Open Questions

1. **Display name for `Actor.FromSystem("bootstrap")` in audit log UI**
   - What we know: The switch default arm uses the raw identifier as display name → "bootstrap"
   - What's unclear: Whether operator-visible audit log UI needs a friendlier label
   - Recommendation: Add `"bootstrap" => "CLI Bootstrap"` to the switch in `Actor.cs` — trivial one-line change, cleaner output

2. **Whether to update `IsFirstRunAsync` in AuthService to use `AnyUsersExistAsync()`**
   - What we know: Currently calls `GetUserCountAsync() == 0` (COUNT scan)
   - What's unclear: No perf concern in practice (called once on first registration attempt, low traffic path)
   - Recommendation: Update it — keeps the codebase consistent and removes the COUNT scan

---

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | NUnit 3 |
| Config file | none (convention-based discovery) |
| Quick run command | `dotnet test TelegramGroupsAdmin.UnitTests --no-build` |
| Full suite command | `dotnet test --no-build` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| BOOT-01 | `--bootstrap <path>` creates Owner account when no users exist | unit | `dotnet test TelegramGroupsAdmin.UnitTests --no-build --filter "BootstrapTests"` | Wave 0 |
| BOOT-02 | Exits 0 with log when users already exist (no file needed) | unit | `dotnet test TelegramGroupsAdmin.UnitTests --no-build --filter "BootstrapTests"` | Wave 0 |
| BOOT-03 | Exits 1 for missing/empty/invalid JSON file | unit | `dotnet test TelegramGroupsAdmin.UnitTests --no-build --filter "BootstrapTests"` | Wave 0 |
| BOOT-04 | UserRecord has EmailVerified=true | unit | `dotnet test TelegramGroupsAdmin.UnitTests --no-build --filter "BootstrapTests"` | Wave 0 |
| BOOT-05 | UserRecord has TotpEnabled=true, TotpSecret=null | unit | `dotnet test TelegramGroupsAdmin.UnitTests --no-build --filter "BootstrapTests"` | Wave 0 |
| BOOT-06 | Audit log written; audit failure is non-fatal (exit 0) | unit | `dotnet test TelegramGroupsAdmin.UnitTests --no-build --filter "BootstrapTests"` | Wave 0 |
| BOOT-07 | Ordering (after migrations, before ML) | manual-only | n/a — compile-time structure, verified by code review | n/a |

**BOOT-07 justification for manual-only:** The ordering requirement is enforced by where the `if (args.Contains("--bootstrap"))` block appears in Program.cs relative to `RunDatabaseMigrationsAsync()` and the ML training block. This is a structural/positional property of Program.cs that is verified by reading the file, not by a runtime test. An integration test could call `--bootstrap` end-to-end but that is much heavier than the unit test approach and adds no validation value given the clear linear structure of Program.cs.

**Test approach for BOOT-01 through BOOT-06:** The bootstrap logic will be extracted into a standalone `async Task BootstrapOwnerAsync(string[] args, WebApplication app)` helper method (or similar) so it can be exercised by unit tests that mock `IUserRepository`, `IPasswordHasher`, and `IAuditService` via NSubstitute. This is the same approach the codebase uses for other service-level unit tests.

### Sampling Rate
- **Per task commit:** `dotnet test TelegramGroupsAdmin.UnitTests --no-build`
- **Per wave merge:** `dotnet test --no-build`
- **Phase gate:** Full suite green before `/gsd:verify-work`

### Wave 0 Gaps
- [ ] `TelegramGroupsAdmin.UnitTests/Services/Auth/BootstrapTests.cs` — covers BOOT-01 through BOOT-06
- [ ] No new framework install needed — NSubstitute + NUnit already present in UnitTests project

---

## Sources

### Primary (HIGH confidence)
- `TelegramGroupsAdmin/Program.cs` lines 186-273 — direct template for CLI flag pattern
- `TelegramGroupsAdmin/Services/AuthService.cs` lines 262-305 — `CreateOwnerAccountAsync` as source of truth for UserRecord field values
- `TelegramGroupsAdmin/Repositories/IUserRepository.cs` — interface that needs `AnyUsersExistAsync()` added
- `TelegramGroupsAdmin/Repositories/UserRepository.cs` lines 21-25 — `GetUserCountAsync` as basis for `AnyUsersExistAsync`
- `TelegramGroupsAdmin.Core/Models/AuditEventType.cs` — `UserRegistered = 13` for audit event
- `TelegramGroupsAdmin.Core/Models/Actor.cs` — `FromSystem()` factory method
- `TelegramGroupsAdmin.Core/Models/UserRecord.cs` — full field list for constructor
- `TelegramGroupsAdmin.Data/CLAUDE.md` — confirms `AuditEventType` dual-enum pattern (Core + Data must stay in sync)

### Secondary (MEDIUM confidence)
- EF Core 10.0 `AnyAsync()` documentation — generates `SELECT EXISTS(...)`, confirmed by EF Core source-code behavior and .NET community consensus

### Tertiary (LOW confidence)
- None

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all libraries are in-project, no new packages needed
- Architecture: HIGH — directly mirrors existing `--backup`/`--restore` pattern with zero uncertainty
- Pitfalls: HIGH — identified from code reading, not speculation

**Research date:** 2026-03-18
**Valid until:** 2026-06-18 (stable — all findings are internal code analysis, not external API documentation)
