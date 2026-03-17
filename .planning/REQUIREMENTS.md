# Requirements: TelegramGroupsAdmin

**Defined:** 2026-03-16
**Core Value:** Reliable, automated Telegram group moderation with a responsive web UI — correctness and operational simplicity above all

## v1.1 Requirements

Requirements for bug fix milestone. Each maps to roadmap phases.

### Data Layer

- [ ] **DATA-01**: All repositories use IDbContextFactory instead of scoped AppDbContext (#326)
- [ ] **DATA-02**: TelegramUserRepository.UpsertAsync handles concurrent upserts without race conditions (#204)

### Backend Services

- [ ] **BACK-01**: Health orchestrator CheckHealthAsync and MarkInactiveAsync are wired up and invoked (#333)
- [ ] **BACK-02**: Three spurious startup/runtime log warnings are eliminated (#309)
- [ ] **BACK-03**: When a message is manually marked as spam, its media is downloaded (if not already cached) to populate image_training_samples (#262)

### Frontend

- [ ] **FRONT-01**: Analytics overview card percentages recalculate when time range selection changes (#384)
- [ ] **FRONT-02**: Timezone detection JS interop handles Blazor prerendering without errors (#203)

## Future Requirements

Deferred to future milestones. Tracked but not in current roadmap.

(None — all selected bugs are in scope)

## Out of Scope

Explicitly excluded. Documented to prevent scope creep.

| Feature | Reason |
|---------|--------|
| New features or enhancements | Bug-fix-only milestone |
| Refactoring beyond what's needed to fix the bug | Keep fixes minimal and focused |
| Test coverage for unrelated code | Only add tests that verify the bug fix |
| IFileScanResultRepository.CleanupExpiredResultsAsync (#398) | Enhancement, not bug |
| IBlocklistSubscriptionsRepository.FindByUrlAsync (#399) | Enhancement, not bug |
| ContentCheckMetadata population (#400) | Enhancement, not bug |
| Enum varchar→smallint migration (#403) | Refactoring, not bug |

## Traceability

Which phases cover which requirements. Updated during roadmap creation.

| Requirement | Phase | Status |
|-------------|-------|--------|
| DATA-01 | — | Pending |
| DATA-02 | — | Pending |
| BACK-01 | — | Pending |
| BACK-02 | — | Pending |
| BACK-03 | — | Pending |
| FRONT-01 | — | Pending |
| FRONT-02 | — | Pending |

**Coverage:**
- v1.1 requirements: 7 total
- Mapped to phases: 0
- Unmapped: 7 ⚠️

---
*Requirements defined: 2026-03-16*
*Last updated: 2026-03-16 after initial definition*
