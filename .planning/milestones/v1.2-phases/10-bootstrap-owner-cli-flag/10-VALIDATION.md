---
phase: 10
slug: bootstrap-owner-cli-flag
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-03-18
---

# Phase 10 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | NUnit 3 |
| **Config file** | none (convention-based discovery) |
| **Quick run command** | `dotnet test TelegramGroupsAdmin.UnitTests --no-build` |
| **Full suite command** | `dotnet test --no-build` |
| **Estimated runtime** | ~5 seconds (unit tests only) |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test TelegramGroupsAdmin.UnitTests --no-build`
- **After every plan wave:** Run `dotnet test --no-build`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 5 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 10-01-01 | 01 | 1 | BOOT-01 | unit | `dotnet test TelegramGroupsAdmin.UnitTests --no-build --filter "BootstrapOwner"` | ❌ W0 | ⬜ pending |
| 10-01-02 | 01 | 1 | BOOT-02 | unit | `dotnet test TelegramGroupsAdmin.UnitTests --no-build --filter "BootstrapOwner"` | ❌ W0 | ⬜ pending |
| 10-01-03 | 01 | 1 | BOOT-03 | unit | `dotnet test TelegramGroupsAdmin.UnitTests --no-build --filter "BootstrapOwner"` | ❌ W0 | ⬜ pending |
| 10-01-04 | 01 | 1 | BOOT-04 | unit | `dotnet test TelegramGroupsAdmin.UnitTests --no-build --filter "BootstrapOwner"` | ❌ W0 | ⬜ pending |
| 10-01-05 | 01 | 1 | BOOT-05 | unit | `dotnet test TelegramGroupsAdmin.UnitTests --no-build --filter "BootstrapOwner"` | ❌ W0 | ⬜ pending |
| 10-01-06 | 01 | 1 | BOOT-06 | unit | `dotnet test TelegramGroupsAdmin.UnitTests --no-build --filter "BootstrapOwner"` | ❌ W0 | ⬜ pending |
| 10-01-07 | 01 | 1 | BOOT-07 | manual | n/a — positional verification in Program.cs | n/a | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `TelegramGroupsAdmin.UnitTests/Services/Auth/BootstrapOwnerServiceTests.cs` — stubs for BOOT-01 through BOOT-06
- [ ] No new framework install needed — NSubstitute + NUnit already present in UnitTests project

*Existing infrastructure covers framework requirements.*

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Bootstrap block positioned after migrations, before ML training | BOOT-07 | Structural/positional property of Program.cs — verified by reading the file, not by a runtime test | Inspect Program.cs: `--bootstrap` block must appear after `RunDatabaseMigrationsAsync()` and before ML training block |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 5s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
