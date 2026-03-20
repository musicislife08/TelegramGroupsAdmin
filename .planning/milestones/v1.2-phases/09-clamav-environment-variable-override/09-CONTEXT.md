# Phase 9: ClamAV Environment Variable Override - Context

**Gathered:** 2026-03-18
**Status:** Ready for planning

<domain>
## Phase Boundary

Override ClamAV daemon connection (host/port) via environment variables for shared daemon topology. Both `CLAMAV_HOST` and `CLAMAV_PORT` must be set for override to activate — partial override falls back to DB config. Override applies at scan time (per-scan), not cached at startup.

</domain>

<decisions>
## Implementation Decisions

### Override mechanism
- Intercept in `ClamAVScannerService.CreateClamClientAsync()` — the single point where `ClamClient` is constructed
- Check both `CLAMAV_HOST` and `CLAMAV_PORT` env vars; if both present, use them; if either missing, fall back to DB config via `GetConfigAsync()`
- Override is read per-scan (every call to `CreateClamClientAsync`), not cached at startup — matches existing per-scan DB config read pattern

### Startup behavior
- Fail-open: no startup validation of ClamAV connection when env vars are set
- ClamAV may start after TGA (container ordering); existing retry/fail-open behavior in `ScanFileAsync` handles transient unavailability
- No change to startup sequence

### Logging
- INFO log on first scan that uses env var override, showing effective host:port
- No log spam on subsequent scans — log once, not per-scan

### Claude's Discretion
- Exact log message wording
- Whether to use a `bool _hasLoggedOverride` flag or similar for one-time logging
- Test approach for verifying override behavior

</decisions>

<specifics>
## Specific Ideas

- "We are going to add an override in the CreateClamClientAsync call. If env exists then use that, otherwise fall back to config."
- Both env vars required — no partial override, prevents accidental misconfiguration where only host changes but port stays default

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- `ClamAVScannerService.CreateClamClientAsync()`: Single insertion point, currently reads from `ISystemConfigRepository.GetAsync()` then constructs `new ClamClient(host, port)`
- `ClamAVScannerService.GetConfigAsync()`: Helper that calls `_configRepository.GetAsync(chatId: null)` — returns `FileScanningConfig` with `Tier1.ClamAV.Host` and `Tier1.ClamAV.Port`

### Established Patterns
- Per-scan config read: `GetConfigAsync()` is called on every scan, not cached — env var override follows the same pattern
- Fail-open on error: `ScanFileAsync` returns `IsClean = true` on connection failures — no behavioral change needed
- Retry with exponential backoff: existing retry logic in `ScanFileAsync` handles transient ClamAV failures

### Integration Points
- `CreateClamClientAsync()` is called by both `ScanFileAsync()` and `GetHealthAsync()` — single change automatically covers both code paths
- No changes needed to `ISystemConfigRepository`, `FileScanningConfig`, or `ClamAVConfig` models
- No changes to Settings UI (UX-01 deferred)

</code_context>

<deferred>
## Deferred Ideas

- Settings UI "(overridden by env var)" badge — UX-01, future milestone
- Startup ping validation when env vars set — decided against (fail-open)

</deferred>

---

*Phase: 09-clamav-environment-variable-override*
*Context gathered: 2026-03-18*
