# Phase 6: Data Layer Fixes - Context

**Gathered:** 2026-03-16
**Status:** Ready for planning

<domain>
## Phase Boundary

Fix database access correctness in the data layer. Migrate 7 remaining repositories from scoped AppDbContext to IDbContextFactory (#326), and fix the read-then-write race condition in TelegramUserRepository.UpsertAsync (#204). No new features, no API changes beyond what's needed for the fix.

</domain>

<decisions>
## Implementation Decisions

### Upsert strategy (DATA-02)
- Use PostgreSQL `ON CONFLICT DO UPDATE` via parameterized `ExecuteSqlAsync` with `FormattableString`
- Raw SQL is acceptable here because EF Core has no built-in UPSERT abstraction
- Parameterized queries only — no string interpolation into SQL
- Static field list matching current update behavior: update Username, FirstName, LastName, UserPhotoPath, PhotoHash, IsActive, LastSeenAt, UpdatedAt
- Do NOT update IsTrusted or BotDmEnabled — those are controlled by dedicated methods (TrustUserAsync/UntrustUserAsync, EnableBotDmAsync/DisableBotDmAsync)
- Sparse/dirty-field tracking deferred — would require API redesign beyond bug-fix scope

### IDbContextFactory migration (DATA-01)
- 7 repos need mechanical transformation (ConfigRepository already done):
  1. `BlocklistSubscriptionsRepository` (ContentDetection)
  2. `DomainFiltersRepository` (ContentDetection)
  3. `CachedBlockedDomainsRepository` (ContentDetection)
  4. `TagDefinitionsRepository` (Telegram)
  5. `PendingNotificationsRepository` (Telegram)
  6. `AdminNotesRepository` (Telegram)
  7. `UserTagsRepository` (Telegram)
- Pattern: replace `(AppDbContext context)` constructor with `(IDbContextFactory<AppDbContext> contextFactory)`, add `await using var context = await contextFactory.CreateDbContextAsync(ct)` per method

### Migration scope and audit
- Migrate the 7 known repos from #326
- Full audit: grep for any non-repository code injecting AppDbContext directly
- If non-repo AppDbContext usage found: create a separate GitHub issue (violates repository pattern)

### Testing strategy
- **DATA-01**: Integration smoke tests in `TelegramGroupsAdmin.IntegrationTests` — one test per migrated repo doing a basic CRUD operation against Testcontainers PostgreSQL to prove the factory pattern works
- **DATA-02**: Integration test attempting concurrent UpsertAsync calls via `Task.WhenAll` against Testcontainers PostgreSQL. If concurrent testing proves flaky, fall back to sequential idempotency test (upsert same user twice, assert one row)

### Claude's Discretion
- Exact concurrency level for the UpsertAsync race condition test
- Whether to use `Task.WhenAll` or sequential idempotency based on test reliability
- DI registration updates if needed (IDbContextFactory is already registered via AddPooledDbContextFactory)

</decisions>

<specifics>
## Specific Ideas

- User prefers EF Core over raw SQL as a general rule — raw SQL only when EF Core doesn't expose the PostgreSQL feature
- Parameterization is critical — user explicitly flagged sanitization/security concerns with raw SQL
- User wants honest pushback on ideas that don't work with the current architecture (sparse updates was correctly rejected)

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- `IDbContextFactory<AppDbContext>` already registered via `AddPooledDbContextFactory` in `ServiceCollectionExtensions.cs`
- 33 existing repos already use the IDbContextFactory pattern — established pattern to follow
- `ConfigRepository` (Configuration project) was already migrated and serves as a reference implementation

### Established Patterns
- All repos use primary constructor injection: `public class FooRepository(IDbContextFactory<AppDbContext> contextFactory)`
- Every method creates its own context: `await using var context = await contextFactory.CreateDbContextAsync(ct)`
- `AsNoTracking()` used on all read queries
- `.ToModel()` / `.ToDto()` extension methods for mapping between Data and UI models

### Integration Points
- Existing integration tests in `TelegramGroupsAdmin.IntegrationTests/Repositories/` use Testcontainers PostgreSQL — follow same test infrastructure
- `TelegramUserRepository.UpsertAsync` is called by `FetchUserPhotoJob` and message processing pipeline

</code_context>

<deferred>
## Deferred Ideas

- Sparse/dirty-field update tracking for UpsertAsync — would require API redesign, open separate issue if desired
- Non-repository code accessing DB directly (if found during audit) — create separate GitHub issue

</deferred>

---

*Phase: 06-data-layer-fixes*
*Context gathered: 2026-03-16*
