---
phase: 12
slug: clamav-ioptions-refactor-doc-fixes
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-03-19
---

# Phase 12 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | NUnit 4.x (TelegramGroupsAdmin.UnitTests) |
| **Config file** | none (implicit via NUnit3TestAdapter) |
| **Quick run command** | `dotnet test TelegramGroupsAdmin.UnitTests --no-build --filter "FullyQualifiedName~ClamAVScannerServiceTests"` |
| **Full suite command** | `dotnet test TelegramGroupsAdmin.UnitTests --no-build` |
| **Estimated runtime** | ~2 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test TelegramGroupsAdmin.UnitTests --no-build --filter "FullyQualifiedName~ClamAVScannerServiceTests"`
- **After every plan wave:** Run `dotnet test TelegramGroupsAdmin.UnitTests --no-build`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 2 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 12-01-01 | 01 | 1 | CLAM-01 | unit | `dotnet test ... --filter ClamAVScannerServiceTests` | Exists (update constants) | pending |
| 12-01-02 | 01 | 1 | CLAM-02 | unit | same | Exists (update constants) | pending |
| 12-01-03 | 01 | 1 | CLAM-03 | unit | same | Exists (update constants) | pending |
| 12-01-04 | 01 | 1 | CLAM-04 | unit | same | Exists (update constants) | pending |

*Status: pending*

---

## Wave 0 Requirements

Existing infrastructure covers all phase requirements. No new test files needed — update 2 string constants in existing test file.

---

## Manual-Only Verifications

All phase behaviors have automated verification.

---

## Validation Sign-Off

- [ ] All tasks have automated verify
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 2s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
