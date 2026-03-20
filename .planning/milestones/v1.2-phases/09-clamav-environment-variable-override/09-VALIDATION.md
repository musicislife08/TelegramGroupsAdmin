---
phase: 9
slug: clamav-environment-variable-override
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-03-18
---

# Phase 9 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | NUnit 3 (TelegramGroupsAdmin.UnitTests) |
| **Config file** | none (implicit via NUnit3TestAdapter) |
| **Quick run command** | `dotnet test TelegramGroupsAdmin.UnitTests --no-build --filter "FullyQualifiedName~ClamAVScannerServiceTests"` |
| **Full suite command** | `dotnet test TelegramGroupsAdmin.UnitTests --no-build` |
| **Estimated runtime** | ~2 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test TelegramGroupsAdmin.UnitTests --no-build --filter "FullyQualifiedName~ClamAVScannerServiceTests"`
- **After every plan wave:** Run `dotnet test TelegramGroupsAdmin.UnitTests --no-build`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 5 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 09-01-01 | 01 | 1 | CLAM-01 | unit | `dotnet test TelegramGroupsAdmin.UnitTests --no-build --filter "FullyQualifiedName~ClamAVScannerServiceTests"` | ❌ W0 | ⬜ pending |
| 09-01-02 | 01 | 1 | CLAM-02 | unit | (same filter) | ❌ W0 | ⬜ pending |
| 09-01-03 | 01 | 1 | CLAM-03 | unit | (same filter) | ❌ W0 | ⬜ pending |
| 09-01-04 | 01 | 1 | CLAM-04 | unit | (same filter) | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `TelegramGroupsAdmin.UnitTests/ContentDetection/ClamAVScannerServiceTests.cs` — stubs for CLAM-01, CLAM-02, CLAM-03, CLAM-04

*Existing test infrastructure covers all other phase requirements — only the new test file is missing.*

---

## Manual-Only Verifications

*All phase behaviors have automated verification.*

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 5s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
