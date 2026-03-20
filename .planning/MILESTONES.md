# Milestones

## v1.2 SaaS Hosting Readiness (Shipped: 2026-03-20)

**Phases completed:** 4 phases, 4 plans, 0 tasks

**Key accomplishments:**
- (none recorded)

---

## v1.1 Bug Fix Sweep (Shipped: 2026-03-18)

**Phases completed:** 4 phases, 9 plans | 87 commits, 174 files, +9,926/-3,435 lines
**Timeline:** 2026-03-16 → 2026-03-18 (3 days)

**Key accomplishments:**
- Migrated 7 repositories from scoped AppDbContext to IDbContextFactory — eliminates ObjectDisposedException in background services
- Rewrote TelegramUserRepository.UpsertAsync with atomic PostgreSQL ON CONFLICT — fixes concurrent race condition
- Wired ChatHealthRefreshOrchestrator CheckHealthAsync/MarkInactiveAsync with 3-strike failure tracking
- Fixed analytics growth percentages; replaced redundant DailyAverageGrowthPercent with PreviousDailyAverage for meaningful comparison
- Moved timezone detection to MainLayout cascade — eliminates JSException during Blazor prerender
- Fixed UpsertAsync is_active INSERT regression, restored upsert logging to Information level
- Eliminated 3 spurious startup/runtime log warnings, added defensive media download on spam marking

---

## v1.0 Dead Code Removal (Shipped: 2026-03-17)

**Phases completed:** 5 phases, 8 plans, 0 tasks

**Key accomplishments:**
- (none recorded)

---

