# Phase 10: Bootstrap Owner CLI Flag - Context

**Gathered:** 2026-03-18
**Status:** Ready for planning

<domain>
## Phase Boundary

Headless Owner account creation via `--bootstrap <path>` CLI flag for Kubernetes init container pattern. JSON file at the specified path provides email and password. Idempotent: exits 0 if any user already exists. Runs after migrations, before ML training.

</domain>

<decisions>
## Implementation Decisions

### Password delivery
- Single `--bootstrap <path>` flag (no inline args for email/password)
- JSON file at the specified path contains both email and password: `{"email": "...", "password": "..."}`
- File-based delivery designed for K8s Secret volume mounts with `optional: true`
- DB-first idempotency check runs before reading the file — file only needed on first bootstrap
- Orchestrator handles K8s Secret lifecycle (create before deploy, delete after exit 0)
- No env var fallback — file-only delivery

### Idempotency behavior
- New `AnyUsersExistAsync()` repo method using EF Core `AnyAsync()` → `SELECT EXISTS(...)` (stops at first row)
- Any users exist in DB → INFO log "already bootstrapped" → exit 0 immediately (no file read)
- Audit log failure after successful user creation is non-fatal — log warning, still exit 0
- Critical operation is account creation; audit log is best-effort

### Exit code semantics
- Exit 0: owner created successfully, OR users already exist (idempotent skip)
- Exit 1: any error (file not found, empty file, invalid JSON, missing fields, invalid email, DB failure, user creation failure)
- All error details go to log output — K8s only cares about 0 vs non-zero for restart policy
- Matches existing `--backup`/`--restore` error handling pattern

### Input validation
- Basic email validation: non-empty and contains `@` — sanity check, not RFC 5322
- No password strength validation — operator's responsibility, not TGA's concern

### Account creation
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

</decisions>

<specifics>
## Specific Ideas

- "These are going to be used by an init container in kube. This password needs to live only long enough to bootstrap the first admin user but disappear securely after."
- K8s Secret mounted with `optional: true` — Secret can be deleted after first bootstrap, init container still starts on subsequent pod reschedules because DB-first check exits 0 before reading file
- JSON file approach chosen over separate CLI args to keep everything in one ephemeral K8s Secret mount

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- `AuthService.CreateOwnerAccountAsync()` (line 265): Battle-tested owner creation with `TotpEnabled=true, TotpSecret=null` pattern — bootstrap can reuse this logic or mirror it
- `IPasswordHasher.HashPassword()`: Existing password hashing service
- `IAuditService.LogEventAsync()`: Audit logging with `Actor.FromSystem()` pattern
- `UserRepository.GetUserCountAsync()`: Basis for new `AnyUsersExistAsync()` method
- `UserRepository.CreateAsync()`: Existing user creation path

### Established Patterns
- CLI flags in Program.cs (lines 186-273): `args.Contains()` check → validate → resolve services from DI → do work → `Environment.Exit()`
- `--backup <path>` / `--restore <path>`: Same flag-with-path-argument pattern bootstrap will use
- `UserRecord` construction: Full record with all fields, `WebUserIdentity` embedded

### Integration Points
- Program.cs between migration (line 190) and ML training (line 277) — BOOT-07 ordering
- `IUserRepository` interface needs `AnyUsersExistAsync()` added
- `AuditEventType` enum — may need `OwnerBootstrapped` or reuse `UserRegistered`

</code_context>

<deferred>
## Deferred Ideas

- EXTBOOT-01: Full configuration seeding via CLI (bot token, OpenAI key, etc.) — future milestone
- EXTBOOT-02: `--bootstrap --skip-totp` flag for fully headless environments — future milestone

</deferred>

---

*Phase: 10-bootstrap-owner-cli-flag*
*Context gathered: 2026-03-18*
