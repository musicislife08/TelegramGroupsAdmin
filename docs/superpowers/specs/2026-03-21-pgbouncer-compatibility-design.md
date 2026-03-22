# PgBouncer Compatibility for TGA Hosting Platform

## Context

TGA is being deployed on a hosting platform where multiple tenant instances share a single PostgreSQL server through PgBouncer in **transaction mode**. Each tenant gets their own database. PgBouncer (v1.25.1, `ghcr.io/icoretech/pgbouncer-docker:1.25.1`) pools connections across all tenant databases to reduce total PostgreSQL connections.

### Problem

Npgsql sends `DISCARD ALL` when returning connections to its internal pool, which conflicts with PgBouncer's own connection state management in transaction mode. The app needs `No Reset On Close=true` in the connection string to prevent this.

### Migration Locking — Not a Problem

Earlier versions of Npgsql used session-level advisory locks (`pg_try_advisory_lock`) for migration concurrency, which broke in PgBouncer transaction mode. **Npgsql 10.0 uses `LOCK TABLE "__EFMigrationsHistory" IN ACCESS EXCLUSIVE MODE`** — a transaction-scoped table lock with `LockReleaseBehavior.Transaction`. Since PgBouncer transaction mode holds the same server connection for the duration of a transaction, this lock is fully compatible. No migration lock override is needed.

### Non-Problems

These components work through PgBouncer transaction mode without app changes:

- **EF Core migrations** — Npgsql 10.0 uses transaction-scoped table locks, not session-scoped advisory locks. Compatible with PgBouncer transaction mode.
- **MigrationHistoryCompactionService** — runs before `MigrateAsync()` using `OpenConnectionAsync()` with explicit `BeginTransactionAsync()`. PgBouncer holds the connection for the transaction duration.
- **IDbContextFactory pattern** — short-lived contexts with transaction-scoped connections; ideal for transaction mode
- **Quartz.NET** — has its own connection pool; PgBouncer 1.25.1 supports protocol-level prepared statements in transaction mode. Npgsql manages its own prepared statement lifecycle; PgBouncer's default `max_prepared_statements = 0` means it does not interfere with Npgsql's internal management.
- **DatabaseMaintenanceJob** — `VACUUM`/`ANALYZE` run as single statements outside transactions; PgBouncer holds the connection for the command duration. Operator must configure `query_timeout = 0` (no query timeout) to accommodate the 600s command timeout — the app manages its own timeout via `NpgsqlCommand.CommandTimeout`.
- **Backup/Restore** — long-running operations via `OpenConnectionAsync()`; operator configures `query_timeout = 0`
- **Health checks** — readiness probe validates the full PgBouncer → PostgreSQL path

## Design

### Control Mechanism

Environment variable `PGBOUNCER_MODE=true` — consistent with existing env var pattern (`ENABLE_METRICS`, `SEQ_URL`). When not set, behavior is unchanged (backwards-compatible for homelab/direct connections).

### Change 1: Connection String Modification

**Modified file:** `TelegramGroupsAdmin.Data/Extensions/ServiceCollectionExtensions.cs`

When `PGBOUNCER_MODE` is set, modify the connection string before registering `NpgsqlDataSource`:

- Set `No Reset On Close=true` — prevents Npgsql from sending `DISCARD ALL` when returning connections to its internal pool. PgBouncer handles connection state reset via its own `server_reset_query = DISCARD ALL`.

Use `NpgsqlConnectionStringBuilder` for clean modification rather than string concatenation.

### Change 2: Startup Logging

**Modified file:** `TelegramGroupsAdmin/WebApplicationExtensions.cs`

Log at Information level in `RunDatabaseMigrationsAsync()` whether PgBouncer mode is active. Operators can verify configuration in logs.

## Files Modified

| File | Change |
|------|--------|
| `TelegramGroupsAdmin.Data/Extensions/ServiceCollectionExtensions.cs` | Connection string modification when `PGBOUNCER_MODE` is set |
| `TelegramGroupsAdmin/WebApplicationExtensions.cs` | Startup log line |

## What Does NOT Change

- EF Core migration locking — Npgsql 10.0 table locks are PgBouncer-compatible
- MigrationHistoryCompactionService — transaction-scoped, works through PgBouncer
- Quartz.NET configuration — works through PgBouncer 1.25.1
- DatabaseMaintenanceJob — operator configures PgBouncer timeouts
- Backup/Restore services — operator configures PgBouncer timeouts
- Health checks — validates full PgBouncer → PostgreSQL path
- IDbContextFactory pattern — already PgBouncer-friendly
- EF Core retry policy — 6 retries with 30s max delay, works with PgBouncer transient disconnects

## Testing

### Unit Tests

- Connection string modification: verify `No Reset On Close=true` is appended when `PGBOUNCER_MODE` is set
- Connection string unmodified: verify no changes when `PGBOUNCER_MODE` is not set

### Integration Test (Testcontainers + PgBouncer)

This is the most important deliverable — proves TGA works end-to-end through PgBouncer and catches future regressions.

1. Start PostgreSQL 18 container (existing Testcontainers infrastructure)
2. Start PgBouncer container (`ghcr.io/icoretech/pgbouncer-docker:1.25.1`) in transaction mode pointing at the PostgreSQL container
3. Build `NpgsqlDataSource` with PgBouncer connection string + `No Reset On Close=true`
4. Create `AppDbContext` and run `MigrateAsync()` through PgBouncer
5. Verify schema was created (query a known table)
6. Verify basic EF Core CRUD operations work through PgBouncer

This test catches:
- Migration table lock compatibility with PgBouncer transaction mode
- Npgsql connection string settings behaving correctly
- Any EF Core/Npgsql protocol issues through PgBouncer
- Regressions if future EF Core/Npgsql upgrades change migration locking behavior

## Operator-Side PgBouncer Configuration

Not part of this implementation — provided as a reference for the hosting platform:

```ini
pool_mode = transaction
max_prepared_statements = 0
query_timeout = 0
server_idle_timeout = 600
client_idle_timeout = 0
server_reset_query = DISCARD ALL
```
