---
phase: 11
slug: decouple-prometheus-metrics-endpoint
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-03-19
---

# Phase 11 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | NUnit 4.x (existing) |
| **Config file** | TelegramGroupsAdmin.UnitTests/TelegramGroupsAdmin.UnitTests.csproj |
| **Quick run command** | `dotnet test TelegramGroupsAdmin.UnitTests --no-build --filter "MetricsConfig"` |
| **Full suite command** | `dotnet test TelegramGroupsAdmin.UnitTests --no-build` |
| **Estimated runtime** | ~2 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test TelegramGroupsAdmin.UnitTests --no-build --filter "MetricsConfig"`
- **After every plan wave:** Run `dotnet test TelegramGroupsAdmin.UnitTests --no-build`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 5 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 11-01-01 | 01 | 1 | STAT-01, STAT-02, STAT-03, STAT-04, STAT-05 | build + startup | `dotnet build && dotnet run --migrate-only` | N/A | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

Existing infrastructure covers all phase requirements. The change is localized to Program.cs OTEL registration and endpoint mapping — validated by build + `--migrate-only` startup check.

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| ENABLE_METRICS activates /metrics | STAT-01 | Requires running app with env var set and hitting endpoint | Set `ENABLE_METRICS=true`, start app, curl `/metrics`, verify Prometheus output |
| SEQ_URL still activates /metrics | STAT-03 | Existing behavior, regression check | Set `OTEL_EXPORTER_OTLP_ENDPOINT=http://seq:5341`, start app, verify `/metrics` responds |
| Metrics-only mode (no tracing) | STAT-02 | Requires observing OTEL pipeline state | Set only `ENABLE_METRICS=true`, verify no Seq connection attempts in logs |
| Startup log shows activation source | STAT-04 | Log output inspection | Check startup logs for "via ENABLE_METRICS" or "via SEQ_URL" attribution |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 5s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
