# Database Migration Testing Strategy

## Context & Motivation

### Why Database Migrations Are Our #1 Pain Point

Database migrations are the highest-risk part of our deployment process. Unlike code changes that can be rolled back instantly, failed migrations can leave the database in a partially-migrated state requiring manual intervention. Based on production experience, we've identified three recurring failure patterns:

1. **Data-Aware Migration Failures** - Migrations that assume clean data break when encountering edge cases (e.g., "system" actor breaking FK constraints)
2. **Lazy Constraint Enforcement** - Schema designs that rely on application-level validation instead of database constraints (e.g., duplicate translations allowed due to missing UNIQUE indexes)
3. **Split Environment Coordination** - Dev machine generates migrations, production runs them, creating async workflow and potential schema drift

### Production Issues Encountered

**AddActorExclusiveArcToAuditLog Migration Failure**
- **What happened**: Migration attempted to copy all `actor_user_id` values to `actor_web_user_id`, then add FK constraint to users table
- **Why it failed**: The string "system" doesn't exist in the users table, causing FK constraint creation to fail mid-migration
- **Impact**: Database left in partially-migrated state with new columns but missing constraints
- **Root cause**: Migration didn't filter edge cases before applying constraints

**Duplicate Translations Allowed**
- **What happened**: Found 2 translations for message 22053 despite exclusive arc CHECK constraint
- **Why it failed**: CHECK constraint validated exclusive arc but lacked UNIQUE indexes on message_id/edit_id
- **Impact**: Application relied on upsert pattern as band-aid, manual cleanup required
- **Root cause**: Incomplete constraint design - logical intent not fully enforced at DB level

**Foreign Key Constraint Violations**
- **What happened**: Migrations failed when adding FK constraints to tables with orphaned records
- **Why it failed**: Existing data violated new constraint (deleted parents, invalid references)
- **Impact**: Migration blocked until manual data cleanup performed
- **Root cause**: No validation of existing data before constraint application

### Cost of Migration Failures

- **Downtime**: Production migrations fail → app unavailable during rollback/fix
- **Data Corruption Risk**: Partially-applied migrations can leave schema inconsistent
- **Manual Intervention**: Failed migrations require SSH access, manual SQL, stress
- **Deployment Delays**: Failed migration blocks entire deployment, must fix and retry
- **Confidence Erosion**: Fear of migrations slows feature velocity

### Split Environment Challenges

**Production**: Docker container, no .NET SDK, can only run migrations (`dotnet run --migrate-only`)
**Dev Machine**: Full toolchain, can generate migrations (`dotnet ef migrations add`)

This creates a coordination workflow:
1. Code changes happen on either machine
2. Production Claude documents needed migrations in CLAUDE.md "PENDING MIGRATIONS" section
3. Dev machine pulls changes and generates migration files
4. Migration committed and pushed
5. Production pulls and applies migration

This workflow introduces lag and potential for desync if migrations aren't generated promptly.

## Testing Goals

### Primary Goal: Catch Migration Failures Before Production

Tests should validate that migrations will succeed in production BEFORE we deploy. This means testing with:
- Realistic production-like data (duplicates, orphans, edge cases)
- Real Postgres 17 database (not InMemory - different constraint enforcement)
- Actual migration SQL (not mocked EF Core operations)

### Secondary Goal: Validate Data Compatibility

Migrations often fail not because of syntax errors, but because existing data violates new constraints. Tests should verify:
- UNIQUE constraints don't conflict with existing duplicates
- FK constraints reference existing parent records
- NOT NULL constraints have defaults or existing values
- CHECK constraints pass for all existing data

### Tertiary Goal: Document Migration Data Requirements

Tests serve as living documentation of what data states are valid. When a test fails, it tells us:
- What edge case we missed
- What data cleanup is needed before migration
- What assumptions the migration makes

This is more valuable than comments or docs that drift out of sync.

### Performance Goal: Fast Enough for Pre-Commit Workflow

Tests must run locally in <10 seconds to be viable for pre-commit checks. This means:
- Reusable Testcontainers (start once, run all tests)
- Minimal data seeding (edge cases, not volume)
- Parallel test execution (NUnit built-in support)
- No CI/CD dependency (tests run locally)

## Testing Approach

### Technology Stack

**Testcontainers.PostgreSQL**
- Spins up real Postgres 17 in Docker container
- Isolated database per test run
- Exact production behavior (constraints, indexes, SQL syntax)
- Fast enough for local development (3-5s startup, reused across tests)

**NUnit Test Framework**
- Standard .NET testing framework
- Built-in parallel execution
- Rich assertion library
- Good IDE integration (Rider, Visual Studio)

**Realistic Data Scenarios**
- Seed databases with production-like edge cases
- Test against data that breaks assumptions
- Focus on data patterns that caused actual production failures

### Test Project Structure

```
TelegramGroupsAdmin.Tests/
├── TelegramGroupsAdmin.Tests.csproj
├── Migrations/
│   ├── MigrationSchemaTests.cs (fresh DB schema validation)
│   ├── MigrationDataCompatibilityTests.cs (prod-like data scenarios)
│   └── ForeignKeyConstraintTests.cs (FK cascade validation)
├── Fixtures/
│   ├── PostgresFixture.cs (shared Testcontainer, reused across tests)
│   └── DataScenarios.cs (realistic test data builders)
└── TestHelpers/
    └── MigrationTestHelper.cs (apply migrations, seed data utilities)
```

### Shared Test Fixtures

Tests will share a single PostgreSQL container instance to minimize startup overhead:
- Container starts once at test suite initialization
- Each test gets a fresh database schema (via migrations)
- Container tears down at test suite completion
- Total overhead: ~3-5 seconds per test run (not per test)

### Local Execution Before Commits

Tests are designed to run locally, not as CI/CD gates:
- Developer runs `dotnet test` before committing migration
- Tests catch issues in <10 seconds
- If tests fail, migration is fixed before commit
- No waiting for CI/CD pipeline feedback

## Phase Breakdown

### Phase 1: Infrastructure Setup

**Objective**: Build the foundation for all future tests

**Tasks**:
1. Create `TelegramGroupsAdmin.Tests` project
2. Install NuGet packages:
   - NUnit
   - NUnit3TestAdapter
   - Testcontainers.PostgreSQL
   - Microsoft.EntityFrameworkCore
   - Npgsql.EntityFrameworkCore.PostgreSQL
3. Create `PostgresFixture.cs` - Shared container with OneTimeSetUp/OneTimeTearDown
4. Create `MigrationTestHelper.cs` - Utility methods for seeding data and applying migrations

**Success Criteria**:
- [x] Can spin up Postgres container
- [x] Can apply all migrations to fresh database
- [x] Can tear down container after tests
- [x] Tests run in <10 seconds
- [x] No Docker cleanup issues (containers properly disposed)

**Validation**:
```bash
dotnet test
# Expected: Tests pass, Postgres container starts/stops cleanly
# Expected output: "Test run finished (< 10 seconds)"
```

---

### Phase 2: Critical Migration Tests

**Objective**: Catch the production failures that have already happened, plus validate recent migration logic

#### Test 1: System Actor Migration Routing

**Context**: AddActorExclusiveArcToAuditLog migration failed because it tried to copy "system" actor to actor_web_user_id, then create FK to users table. "system" doesn't exist in users table.

**Test Scenario**:
- Seed audit_log with actor_user_id = "system"
- Apply AddActorExclusiveArcToAuditLog migration
- Verify "system" actors routed to actor_system_identifier, not actor_web_user_id
- Verify FK constraint on actor_web_user_id succeeds

**Data Setup**:
```
audit_log:
  id=1, actor_user_id="system", action="User.Created"
  id=2, actor_user_id="user-123", action="User.Updated"
```

**Expected Results**:
- Migration succeeds
- Row 1: actor_system_identifier="system", actor_web_user_id=NULL
- Row 2: actor_system_identifier=NULL, actor_web_user_id="user-123"
- FK constraint on actor_web_user_id exists and references users.id

**Why This Matters**: This exact scenario caused production downtime. Test ensures it can't happen again.

---

#### Test 2: Duplicate Translation Prevention

**Context**: Found 2 translations for message 22053 despite exclusive arc CHECK constraint. CHECK constraint validated exclusive arc but didn't prevent duplicates.

**Test Scenario**:
- Apply all migrations (including AddUniqueConstraintToMessageTranslations)
- Insert translation for message_id=1
- Attempt to insert second translation for message_id=1
- Verify second insert fails with unique constraint violation

**Data Setup**:
```
messages:
  id=1, chat_id=100, telegram_message_id=200, text="Test"

message_translations:
  id=1, message_id=1, translated_text="First translation"
```

**Expected Results**:
- First translation inserts successfully
- Second translation throws DbUpdateException with unique constraint error
- Database contains only 1 translation for message_id=1

**Why This Matters**: Duplicate translations break manual translate feature (upsert doesn't work if duplicates exist first).

---

#### Test 3: SpamCheckSkipReason Backfill Logic

**Context**: AddSpamCheckSkipReasonToMessages migration (2025-10-25) intelligently backfills existing data based on chat_admins and telegram_users tables to classify why spam checks were skipped.

**Test Scenario**:
- Seed database with production-like message data representing different user types
- Apply AddSpamCheckSkipReasonToMessages migration
- Verify intelligent backfill SQL correctly classified all messages based on admin/trust status
- Verify priority handling (admin status takes precedence over trusted status)

**Data Setup**:
```
users:
  id="admin-user-1", email="admin@test.com"
  id="trusted-user-1", email="trusted@test.com"
  id="regular-user-1", email="regular@test.com"
  id="both-user-1", email="both@test.com"

telegram_users:
  telegram_user_id=1001 (admin-user-1)
  telegram_user_id=2001, is_trusted=true (trusted-user-1)
  telegram_user_id=3001 (regular-user-1)
  telegram_user_id=4001, is_trusted=true (both-user-1)

chat_admins:
  chat_id=100, telegram_id=1001, is_active=true (admin-user-1)
  chat_id=100, telegram_id=4001, is_active=true (both-user-1)

messages:
  message_id=1, user_id=1001, chat_id=100 (no detection_results)
  message_id=2, user_id=2001, chat_id=100 (no detection_results)
  message_id=3, user_id=3001, chat_id=100 (no detection_results)
  message_id=4, user_id=4001, chat_id=100 (no detection_results)
  message_id=5, user_id=5001, chat_id=100

detection_results:
  message_id=5, spam_detected=false (spam was actually checked)
```

**Expected Results**:
- Migration succeeds without errors
- Message 1 (admin only): `spam_check_skip_reason = 2` (UserAdmin)
- Message 2 (trusted only): `spam_check_skip_reason = 1` (UserTrusted)
- Message 3 (regular user): `spam_check_skip_reason = 0` (NotSkipped - old data assumption)
- Message 4 (both admin and trusted): `spam_check_skip_reason = 2` (UserAdmin - admin priority)
- Message 5 (has detection_result): `spam_check_skip_reason = 0` (NotSkipped - was actually checked)

**Why This Matters**: Validates complex data-dependent backfill logic. Similar intelligent migrations in the future can follow this pattern, and this test ensures the priority handling (admin > trusted > default) works correctly.

---

#### Test 4: Orphaned Foreign Key Protection

**Context**: Migrations adding FK constraints failed because existing data had orphaned references (deleted parents).

**Test Scenario**:
- Seed message_translations with message_id that doesn't exist in messages table
- Attempt to add FK constraint from message_translations.message_id to messages.id
- Verify constraint creation fails (or orphan cleanup runs first)

**Data Setup**:
```
messages:
  (empty table)

message_translations:
  id=1, message_id=999, translated_text="Orphaned translation"
```

**Expected Results**:
- FK constraint creation fails with referential integrity violation
- OR migration includes orphan cleanup step that deletes row before adding constraint
- Database schema remains consistent (no partial migration)

**Why This Matters**: Orphaned FKs are common in production (deleted messages, users, chats). Migrations must handle them gracefully.

---

#### Test 5: NULL Exclusive Arc Validation

**Context**: Exclusive arc CHECK constraints should allow one NULL value (either message_id OR edit_id can be NULL, but not both).

**Test Scenario**:
- Apply migrations with exclusive arc CHECK constraint
- Insert message_translation with message_id=1, edit_id=NULL (valid)
- Insert message_translation with message_id=NULL, edit_id=1 (valid)
- Attempt to insert with message_id=NULL, edit_id=NULL (invalid)
- Attempt to insert with message_id=1, edit_id=1 (invalid)

**Data Setup**:
```
messages:
  id=1, chat_id=100, telegram_message_id=200, text="Test"

message_edits:
  id=1, message_id=1, edit_sequence=1, edited_text="Edited"
```

**Expected Results**:
- message_id=1, edit_id=NULL: Success
- message_id=NULL, edit_id=1: Success
- message_id=NULL, edit_id=NULL: Fails CHECK constraint
- message_id=1, edit_id=1: Fails CHECK constraint

**Why This Matters**: Exclusive arc pattern is used in multiple tables (audit_log, message_translations). Must be validated correctly.

---

**Success Criteria for Phase 2**:
- [x] All 5 critical tests pass
- [x] System actor migration routing validated
- [x] Duplicate translation prevention enforced
- [x] SpamCheckSkipReason backfill logic validated
- [x] Orphaned FK protection working
- [x] Exclusive arc CHECK constraint validated

---

### Phase 3: Cascade Behavior Tests

**Objective**: Validate FK cascade rules work as expected, no surprise deletions or orphans

#### Test 5: User Deletion Cascade (audit_log)

**Context**: Deleting a user should handle audit_log entries with actor_web_user_id references.

**Test Scenario**:
- Seed users table with user
- Seed audit_log with entries referencing that user as actor
- Delete user
- Verify audit_log entries handled correctly (SET NULL, CASCADE, or RESTRICT based on design)

**Expected Behavior**: Need to verify what cascade rule is configured (ON DELETE SET NULL likely)

---

#### Test 6: Message Deletion Cascade (message_translations)

**Context**: Deleting a message should cascade to message_translations.

**Test Scenario**:
- Seed messages table with message
- Seed message_translations with translation for that message
- Delete message
- Verify translation is deleted automatically (ON DELETE CASCADE)

**Expected Results**:
- Message deleted successfully
- Translation automatically deleted
- No orphaned message_translations records

---

#### Test 7: Chat Deletion Cleanup (managed_chats)

**Context**: Deleting a chat should clean up managed_chats entries.

**Test Scenario**:
- Seed managed_chats with entries for a chat
- Delete chat (or set is_active=false)
- Verify managed_chats cleanup

**Expected Behavior**: Need to verify cascade rules or application-level cleanup

---

**Success Criteria for Phase 3**:
- [x] All cascade behaviors documented and tested
- [x] No surprise deletions
- [x] No orphaned records after cascades
- [x] Application logic + DB constraints work together

---

### Phase 4: Data Integrity Tests

**Objective**: Verify all constraints are enforced at database level, not just application level

#### Test 8: UNIQUE Constraints Enforced

**Test Coverage**:
- message_translations: UNIQUE on message_id (partial index: WHERE message_id IS NOT NULL)
- message_translations: UNIQUE on edit_id (partial index: WHERE edit_id IS NOT NULL)
- Other tables with UNIQUE constraints

**Test Approach**:
- Apply migrations
- Insert valid record
- Attempt to insert duplicate
- Verify database rejects with unique constraint violation (not application-level validation)

---

#### Test 9: CHECK Constraints Enforced

**Test Coverage**:
- message_translations: Exclusive arc (message_id XOR edit_id)
- audit_log: Exclusive arc (actor_web_user_id XOR actor_system_identifier)
- Other tables with CHECK constraints

**Test Approach**:
- Attempt to insert records violating CHECK constraint
- Verify database rejects (not application-level validation)

---

#### Test 10: NOT NULL Constraints Enforced

**Test Coverage**:
- Required fields across all tables

**Test Approach**:
- Attempt to insert records with NULL in NOT NULL columns
- Verify database rejects

---

#### Test 11: FK Constraints Enforced

**Test Coverage**:
- All foreign key relationships

**Test Approach**:
- Attempt to insert records referencing non-existent parents
- Verify database rejects with FK violation

---

**Success Criteria for Phase 4**:
- [x] All UNIQUE constraints validated
- [x] All CHECK constraints validated
- [x] All NOT NULL constraints validated
- [x] All FK constraints validated
- [x] Database enforces constraints, not just application code

---

### Phase 5: Migration Workflow Tests

**Objective**: Validate schema evolution and rollback safety

#### Test 12: Migration Ordering

**Test Scenario**:
- Fresh database (no tables)
- Apply all migrations in order
- Verify no dependency errors (FKs created after tables, etc.)

**Expected Results**:
- All migrations apply successfully
- No "table does not exist" errors
- No FK dependency errors
- Final schema matches expected state

---

#### Test 13: Rollback Safety (Down Migrations)

**Test Scenario**:
- Apply migration
- Run Down() migration to rollback
- Verify schema reverted to previous state

**Expected Results**:
- Down() migration succeeds
- Schema matches pre-migration state
- No orphaned tables or columns

**Note**: This is often neglected - Down() migrations are rarely tested until disaster strikes.

---

**Success Criteria for Phase 5**:
- [x] All migrations apply in order on fresh DB
- [x] Down() migrations work correctly
- [x] Schema evolution safe and reversible

---

## Test Scenario Catalog

### Critical Scenarios (from production failures)

#### 1. Actor Exclusive Arc Migration
- **Context**: AddActorExclusiveArcToAuditLog failed on "system" actor
- **Test**: Verify system actors route to actor_system_identifier
- **Data**: Audit logs with actor_user_id = "system"
- **Expected**: Migration succeeds, system actors in correct column

#### 2. Duplicate Translation Prevention
- **Context**: Found 2 translations for message 22053
- **Test**: Verify unique indexes prevent duplicates
- **Data**: Multiple translations for same message_id
- **Expected**: Second insert fails with unique constraint violation

#### 3. Orphaned Foreign Key Protection
- **Context**: Migrations failed with FK violations
- **Test**: Verify FK creation validates existing data
- **Data**: message_translation with non-existent message_id
- **Expected**: FK creation fails or orphan cleanup runs first

#### 4. SpamCheckSkipReason Backfill Logic
- **Context**: AddSpamCheckSkipReasonToMessages (2025-10-25) backfills based on admin/trust status
- **Test**: Verify intelligent backfill routes messages correctly with priority handling
- **Data**: Mix of admin, trusted, regular users with/without detection_results
- **Expected**: Admin=2, Trusted=1, Default=0, admin priority over trusted

#### 5. NULL Exclusive Arc Handling
- **Context**: Exclusive arc CHECK constraint edge cases
- **Test**: Verify one NULL allowed, not both NULL
- **Data**: audit_log with NULL actor values
- **Expected**: CHECK constraint allows one NULL, blocks both NULL

---

### High-Priority Scenarios (common patterns)

#### 6. User Deletion Cascade
- **Context**: Deleting users should clean up audit logs
- **Test**: Delete user → verify audit_log cascade
- **Data**: User with audit_log entries
- **Expected**: Audit logs with actor_web_user_id updated/deleted

#### 7. Message Deletion Cascade
- **Context**: Deleting messages should remove translations
- **Test**: Delete message → verify message_translations cascade
- **Data**: Message with translation
- **Expected**: Translation deleted automatically

#### 8. BackgroundJobConfig Seed Data
- **Context**: Default job configs must initialize correctly
- **Test**: Fresh DB → verify all default jobs created
- **Data**: Empty background_job_config table
- **Expected**: 6 default jobs (ChatHealthCheck, ScheduledBackup, DatabaseMaintenance, UserPhotoRefresh, BlocklistSync, MessageCleanup)

#### 9. Chat Cleanup on Deletion
- **Context**: Removing chat should clean up related data
- **Test**: Delete chat → verify managed_chats cleanup
- **Data**: Chat with managed_chats entries
- **Expected**: Related entries cleaned up

---

### Medium-Priority Scenarios (workflow protection)

#### 10. Migration Ordering
- **Context**: Migrations must apply in timestamp order
- **Test**: Fresh DB → all migrations apply sequentially
- **Data**: Empty database
- **Expected**: All migrations succeed, no dependency errors

#### 11. Rollback Safety
- **Context**: Down() migrations must undo Up() changes
- **Test**: Apply migration → rollback → verify schema reverted
- **Data**: Any migration
- **Expected**: Schema matches pre-migration state

---

## Test Data Strategy

### Realistic Production Scenarios

Tests should use data patterns that reflect production reality:

**Duplicate Records**
- Multiple translations for same message (violates UNIQUE constraint)
- Multiple audit logs for same action (may be valid or invalid depending on design)

**Orphaned Foreign Keys**
- message_translations referencing deleted messages
- audit_log referencing deleted users
- managed_chats referencing deleted chats

**Edge Case Values**
- "system" as actor (not a user ID)
- NULL values in exclusive arc columns (one NULL OK, both NULL invalid)
- Empty strings vs NULL (different constraint behavior)

**Seed Data Conflicts**
- BackgroundJobConfigService defaults conflicting with existing data
- Default configs with different IntervalDuration formats

**Constraint Violations**
- UNIQUE: Duplicate message_id in message_translations
- FK: Non-existent parent reference
- NOT NULL: Missing required field
- CHECK: Exclusive arc with both values populated

### Data Seeding Approach

**Helper Methods for Common Scenarios**
- `SeedDuplicateTranslations(messageId)` - Creates 2 translations for same message
- `SeedSystemActor()` - Creates audit_log with actor_user_id = "system"
- `SeedOrphanedFK()` - Creates child record with non-existent parent
- `SeedValidExclusiveArc()` - Creates record with one value, other NULL
- `SeedInvalidExclusiveArc()` - Creates record with both values (should fail)

**Reusable Test Data Builders**
```
MessageBuilder.WithId(1).WithText("Test").Build()
TranslationBuilder.ForMessage(1).WithText("Translation").Build()
AuditLogBuilder.WithSystemActor().WithAction("Created").Build()
```

**Focus on Edge Cases, Not Volume**
- Don't seed 10,000 records to test performance
- Seed the minimum data to trigger edge case
- Keep tests fast (<10s total runtime)

**Avoid Anonymized Production Dumps**
- Privacy concerns
- Large file sizes slow down tests
- Focus on specific edge cases instead

---

## Success Criteria

### Phase 1 Complete
- [x] Can run tests locally
- [x] Postgres container starts/stops cleanly
- [x] Migration helper methods work
- [x] Tests execute in <10 seconds

### Phase 2 Complete
- [x] All 3 production failures caught by tests
- [x] System actor migration routing validated
- [x] Duplicate translation prevention enforced
- [x] Orphaned FK protection working
- [x] Exclusive arc validation correct

### Phase 3 Complete
- [x] All FK cascades validated
- [x] No surprise deletions
- [x] No orphaned records after cascades
- [x] User/Message/Chat deletion safe

### Phase 4 Complete
- [x] All UNIQUE constraints enforced at DB level
- [x] All CHECK constraints enforced at DB level
- [x] All NOT NULL constraints enforced at DB level
- [x] All FK constraints enforced at DB level

### Phase 5 Complete
- [x] All migrations apply in order on fresh DB
- [x] Down() migrations work correctly
- [x] Schema evolution safe and reversible

### Overall Success
- [x] Tests run locally in <10 seconds
- [x] Catch migration issues before commits
- [x] Production deployments succeed without manual fixes
- [x] No more "PENDING MIGRATIONS" sections in CLAUDE.md
- [x] Confidence in database changes restored

---

## Future Enhancements (Post-Phase 5)

### Performance Testing
- Benchmark migration execution time (all migrations on fresh DB)
- Track migration time over project lifespan
- Alert if migration takes >10 seconds (indicates potential production slowdown)

### Anonymized Production Data Seeding
- Export anonymized subset of production data for testing
- Test migrations against realistic data volume
- Validate performance at production scale

### Migration Rollback Automation
- Detect failed migration
- Automatically run Down() migration
- Restore to known good state
- Alert developers of failure

### Schema Drift Detection
- Compare production schema to migration-generated schema
- Detect manual changes not captured in migrations
- Generate migrations to bring schemas in sync

---

## Implementation Tracking

### Checklist

**Phase 1: Infrastructure Setup**
- [ ] Create TelegramGroupsAdmin.Tests project
- [ ] Install NuGet packages (NUnit, Testcontainers.PostgreSQL)
- [ ] Create PostgresFixture.cs (shared container)
- [ ] Create MigrationTestHelper.cs (seed data utilities)
- [ ] Verify: Tests run in <10 seconds

**Phase 2: Critical Migration Tests**
- [ ] Test 1: System Actor Migration Routing
- [ ] Test 2: Duplicate Translation Prevention
- [ ] Test 3: SpamCheckSkipReason Backfill Logic
- [ ] Test 4: Orphaned Foreign Key Protection
- [ ] Test 5: NULL Exclusive Arc Validation

**Phase 3: Cascade Behavior Tests**
- [ ] Test 6: User Deletion Cascade (audit_log)
- [ ] Test 7: Message Deletion Cascade (message_translations)
- [ ] Test 8: Chat Cleanup on Deletion (managed_chats)

**Phase 4: Data Integrity Tests**
- [ ] Test 9: UNIQUE Constraints Enforced
- [ ] Test 10: CHECK Constraints Enforced
- [ ] Test 11: NOT NULL Constraints Enforced
- [ ] Test 12: FK Constraints Enforced

**Phase 5: Migration Workflow Tests**
- [ ] Test 13: Migration Ordering (fresh DB)
- [ ] Test 14: Rollback Safety (Down migrations)

---

### Implementation Approach

**Total Tests**: 14 tests across 5 phases

**Incremental Approach**: Each phase can be tackled independently. Phases build on each other but don't block each other. Phase 1-2 provides foundation and highest ROI (catches actual production failures).

---

## Notes

This document serves as the strategic roadmap for database migration testing. It should be updated as:
- Tests are implemented (check off items in Implementation Tracking)
- New production issues discovered (add to Test Scenario Catalog)
- Test approach refined (update Testing Approach section)
- Success criteria met (update Success Criteria section)

**Living Document**: This is not a one-time planning doc. It evolves with the project and serves as the source of truth for database testing strategy.
