# Phase 12: ClamAV Compose Fix + Doc Fixes - Context

**Gathered:** 2026-03-19
**Status:** Ready for planning

<domain>
## Phase Boundary

Fix the compose file env var names to match what the code actually reads, and fix stale documentation. No code changes — the implementation is correct, only the deployment examples are wrong.

</domain>

<decisions>
## Implementation Decisions

### Override mechanism — keep as-is
- Code reads `CLAMAV_HOST` / `CLAMAV_PORT` (single underscore) via raw `Environment.GetEnvironmentVariable` — this is correct and tested
- IOptions<ClamAVConfig> rejected: the override only needs Host/Port (2 of 4 fields), the other fields (Enabled, TimeoutSeconds) must always come from the database
- No code changes, no constructor changes, no default removal, no DI registration changes

### Compose fix direction
- Fix the compose files to match the code, not the other way around
- `CLAMAV__HOST` → `CLAMAV_HOST` (double underscore was ASP.NET Core IConfiguration convention, but nothing reads ClamAV from IConfiguration)
- `CLAMAV__PORT` → `CLAMAV_PORT` (same)
- The double-underscore names were dead env vars — they fed into IConfiguration but no code consumed them from there

### Doc fixes
- Add `ENABLE_METRICS` commented example to production compose (and development compose as comment)
- Update REQUIREMENTS.md CLAM-01 to remove IOptions reference — it's raw env var override
- STAT-01/02/03 stale SEQ_URL references already fixed

### Claude's Discretion
- Exact comment wording in compose files
- Whether to add a comment explaining that single-underscore is intentional (not the ASP.NET convention)

</decisions>

<specifics>
## Specific Ideas

- "Perhaps just leaving them with a single _ is fine and docs updated is the correct path"
- The override is purely a connection redirect (host:port only) — Enabled and TimeoutSeconds must always come from the database via Settings UI
- IOptions was considered but adds complexity for no benefit here — the code works, just the compose examples have wrong env var names

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- `ClamAVScannerService.GetEffectiveEndpointAsync()`: Already correctly reads single-underscore env vars, no changes needed
- 9 unit tests in `ClamAVScannerServiceTests.cs`: Already use single-underscore constants, no changes needed

### Established Patterns
- Raw `Environment.GetEnvironmentVariable` for infrastructure override (host:port only)
- DB config via `ISystemConfigRepository.GetAsync()` for all other settings (Enabled, TimeoutSeconds)
- One-time INFO log via `static volatile bool _hasLoggedOverride`

### Integration Points
- `examples/compose.development.yml` lines 173-174: `CLAMAV__HOST`/`CLAMAV__PORT` → rename to single underscore
- `examples/compose.production.yml` lines 136-137: same

</code_context>

<deferred>
## Deferred Ideas

- UX-01: Settings UI "(overridden by env var)" badge — future milestone, would benefit from IOptions at that point
- IOptions<ClamAVConfig> binding — revisit if/when a display use case emerges

</deferred>

---

*Phase: 12-clamav-ioptions-refactor-doc-fixes*
*Context gathered: 2026-03-19*
