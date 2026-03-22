# Fix: EF Core Migration Trigger Permission for Non-Superuser

## Problem

Migration `20260225233822_ChangeMessageIdFromBigintToInt` uses `ALTER TABLE ... DISABLE TRIGGER ALL` to temporarily disable FK enforcement during a message_id remap. On PostgreSQL 15+, `DISABLE TRIGGER ALL` disables *system triggers* (including `RI_ConstraintTrigger_*` for referential integrity), which requires SUPERUSER — even if the user owns the table.

In the hosting platform, each tenant gets a non-superuser role (`tga_{instanceId}`) that owns its database. Migrations fail with:

```
ERROR: 42501: permission denied: "RI_ConstraintTrigger_c_18195" is a system trigger
```

This is **not** PgBouncer-related — the same error occurs connecting directly to PostgreSQL as a non-superuser.

## Why Tests Don't Catch This

`PgBouncerFixture.CreateUniqueDatabaseAsync()` creates databases using the default testcontainers `postgres` user, which is a superuser. The `DISABLE TRIGGER ALL` succeeds silently.

## Design

### Part 1: Fix test infrastructure to expose the bug

Update `PgBouncerFixture.CreateUniqueDatabaseAsync()` to mirror production's non-superuser pattern:

1. Create a non-superuser role: `CREATE ROLE tga_test_{guid} WITH LOGIN PASSWORD '...' NOSUPERUSER`
2. Create the database owned by that role: `CREATE DATABASE "test_db_{guid}" OWNER "tga_test_{guid}"`
3. Return connection strings that authenticate as the non-superuser role (both direct and PgBouncer)

The existing `MigrateAsync_ThroughPgBouncer_AppliesAllMigrations` test will now fail with the trigger permission error — confirming the bug is reproduced.

### Part 2: Fix the migration

Replace `DISABLE/ENABLE TRIGGER ALL` with explicit FK constraint drop/recreate. Table owners can `DROP CONSTRAINT` and `ADD CONSTRAINT` on their own tables without SUPERUSER.

**7 FK constraints to drop before remap and recreate after:**

| Constraint Name | Child Table | Column | Delete Behavior |
|---|---|---|---|
| `FK_detection_results_messages_message_id` | detection_results | message_id | CASCADE |
| `FK_message_edits_messages_message_id` | message_edits | message_id | CASCADE |
| `FK_message_translations_messages_message_id` | message_translations | message_id | CASCADE |
| `FK_training_labels_messages_message_id` | training_labels | message_id | CASCADE |
| `FK_user_actions_messages_message_id` | user_actions | message_id | SET NULL |
| `FK_image_training_samples_messages_message_id` | image_training_samples | message_id | CASCADE |
| `FK_video_training_samples_messages_message_id` | video_training_samples | message_id | CASCADE |

Note: `reply_to_message_id` on `messages` has an index but no FK constraint — no action needed for it.

**SQL structure (Up method only — Down has no trigger manipulation).**

All FK drop, remap, and FK recreate statements must remain in a single `migrationBuilder.Sql()` call to maintain transactional atomicity. If the migration fails mid-way, the transaction rolls back and FK constraints are preserved.

```sql
-- Create remap table (unchanged)
CREATE TEMP TABLE _msg_id_remap AS ...;

-- Drop FK constraints (replaces DISABLE TRIGGER ALL)
ALTER TABLE detection_results DROP CONSTRAINT "FK_detection_results_messages_message_id";
ALTER TABLE message_translations DROP CONSTRAINT "FK_message_translations_messages_message_id";
ALTER TABLE training_labels DROP CONSTRAINT "FK_training_labels_messages_message_id";
ALTER TABLE message_edits DROP CONSTRAINT "FK_message_edits_messages_message_id";
ALTER TABLE user_actions DROP CONSTRAINT "FK_user_actions_messages_message_id";
ALTER TABLE image_training_samples DROP CONSTRAINT "FK_image_training_samples_messages_message_id";
ALTER TABLE video_training_samples DROP CONSTRAINT "FK_video_training_samples_messages_message_id";

-- Remap updates (unchanged — same 9 UPDATE statements)

-- Recreate FK constraints (replaces ENABLE TRIGGER ALL)
ALTER TABLE detection_results ADD CONSTRAINT "FK_detection_results_messages_message_id"
    FOREIGN KEY (message_id) REFERENCES messages(message_id) ON DELETE CASCADE;
-- ... (same pattern for all 7, user_actions uses ON DELETE SET NULL)

DROP TABLE _msg_id_remap;
```

### Safety for existing deployments

The migration handles both cases:

- **Fresh databases** (no out-of-range message_ids): The remap temp table is empty. FK constraints are dropped and recreated as a no-op. Zero data impact.
- **Existing databases that already ran this migration**: This migration is already recorded in `__EFMigrationsHistory`. It will not re-run. No action needed.
- **Existing databases that haven't run this migration yet**: The remap proceeds with FK drop/recreate instead of trigger disable. This is the fix.

### PgBouncer auth configuration update

The `pgbouncer.ini` uses `auth_query` to look up user credentials from `pg_shadow`. The new non-superuser role needs to be resolvable through this auth chain. Since the `pgbouncer_auth` role (SUPERUSER) runs the `auth_query`, any PostgreSQL role with a password is automatically discoverable — no `userlist.txt` changes needed.

### Files Modified

- `TelegramGroupsAdmin.IntegrationTests/PgBouncer/PgBouncerFixture.cs` — non-superuser role creation
- `TelegramGroupsAdmin.Data/Migrations/20260225233822_ChangeMessageIdFromBigintToInt.cs` — replace trigger disable with FK drop/recreate

### Testing

1. Run PgBouncer integration tests with non-superuser fixture — verify they fail before the migration fix (TDD red)
2. Apply migration fix — verify tests pass (TDD green)
3. Existing migration tests (`TelegramGroupsAdmin.Tests`) continue to validate schema correctness
