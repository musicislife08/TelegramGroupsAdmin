#!/bin/bash
set -e

# SQLite to PostgreSQL Data Migration Script
# This script exports data from SQLite databases and imports into PostgreSQL

echo "=== TelegramGroupsAdmin: SQLite → PostgreSQL Migration ==="
echo ""

# Configuration
SQLITE_DIR="./TelegramGroupsAdmin/bin/debug/net10.0"
TEMP_DIR="./migration_temp"
DB_HOST="${DB_HOST:-localhost}"
DB_PORT="${DB_PORT:-5432}"
DB_NAME="${DB_NAME:-telegram_groups_admin}"
DB_USER="${DB_USER:-tgadmin}"

# Prompt for password if not set
if [ -z "$DB_PASSWORD" ]; then
    echo "PostgreSQL password not set in DB_PASSWORD environment variable."
    read -sp "Enter password for PostgreSQL user '$DB_USER': " DB_PASSWORD
    echo ""
fi

# Export PGPASSWORD for all psql commands
export PGPASSWORD="$DB_PASSWORD"

# Check prerequisites
command -v sqlite3 >/dev/null 2>&1 || { echo "ERROR: sqlite3 not found. Install with: brew install sqlite" >&2; exit 1; }
command -v psql >/dev/null 2>&1 || { echo "ERROR: psql not found. Install with: brew install postgresql" >&2; exit 1; }

# Create temp directory
mkdir -p "$TEMP_DIR"

echo "Step 1: Checking PostgreSQL connection..."
if ! psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -c '\q' 2>/dev/null; then
    echo "ERROR: Cannot connect to PostgreSQL. Make sure:"
    echo "  1. Docker container is running: docker compose up -d postgres"
    echo "  2. Password is correct"
    echo "  3. Migrations have been run: dotnet run --project TelegramGroupsAdmin"
    exit 1
fi
echo "✓ PostgreSQL connection successful"
echo ""

echo "Step 2: Clearing existing data..."
echo "⚠️  WARNING: This will DELETE ALL existing data in PostgreSQL!"
read -p "Do you want to clear all existing data? (yes/no): " clear_data

if [ "$clear_data" = "yes" ]; then
    echo "  → Truncating all tables..."
    psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" <<'SQL'
-- Disable foreign key checks temporarily
SET session_replication_role = 'replica';

-- Truncate all tables (order doesn't matter with FK disabled)
TRUNCATE TABLE users, invites, audit_log, verification_tokens,
                messages, message_edits, spam_checks,
                stop_words, training_samples, spam_samples, chat_prompts,
                spam_check_configs, spam_detection_configs
                RESTART IDENTITY CASCADE;

-- Re-enable foreign key checks
SET session_replication_role = 'origin';
SQL
    echo "  ✓ All existing data cleared"
else
    echo "  ⊘ Skipping data cleanup - migration may fail on duplicate keys"
fi
echo ""

# Function to export and import table data
migrate_table() {
    local sqlite_db=$1
    local table_name=$2
    local csv_file="$TEMP_DIR/${table_name}.csv"

    echo "  → Exporting $table_name from SQLite..."
    sqlite3 -header -csv "$sqlite_db" "SELECT * FROM $table_name;" > "$csv_file"

    local row_count=$(tail -n +2 "$csv_file" | wc -l | tr -d ' ')

    if [ "$row_count" -eq 0 ]; then
        echo "    ⊘ No data to migrate for $table_name"
        return
    fi

    echo "  → Importing $row_count rows into PostgreSQL..."
    psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -c "\COPY $table_name FROM '$csv_file' WITH CSV HEADER;" > /dev/null 2>&1

    echo "    ✓ Migrated $row_count rows"
}

echo "Step 3: Migrating identity.db tables..."
if [ -f "$SQLITE_DIR/identity.db" ]; then
    migrate_table "$SQLITE_DIR/identity.db" "users"
    migrate_table "$SQLITE_DIR/identity.db" "invites"
    migrate_table "$SQLITE_DIR/identity.db" "audit_log"
    migrate_table "$SQLITE_DIR/identity.db" "verification_tokens"
    echo "✓ Identity database migration complete"
else
    echo "⚠ Warning: identity.db not found at $SQLITE_DIR/identity.db"
fi
echo ""

echo "Step 4: Migrating message_history.db tables..."
if [ -f "$SQLITE_DIR/message_history.db" ]; then
    migrate_table "$SQLITE_DIR/message_history.db" "messages"
    migrate_table "$SQLITE_DIR/message_history.db" "message_edits"
    migrate_table "$SQLITE_DIR/message_history.db" "spam_checks"
    echo "✓ Message history database migration complete"
else
    echo "⚠ Warning: message_history.db not found at $SQLITE_DIR/message_history.db"
fi
echo ""

echo "Step 5: Migrating spam_detection.db tables..."
if [ -f "$SQLITE_DIR/spam_detection.db" ]; then
    migrate_table "$SQLITE_DIR/spam_detection.db" "stop_words"
    migrate_table "$SQLITE_DIR/spam_detection.db" "training_samples"
    migrate_table "$SQLITE_DIR/spam_detection.db" "spam_samples"

    # Handle group_prompts → chat_prompts table rename
    if sqlite3 "$SQLITE_DIR/spam_detection.db" "SELECT name FROM sqlite_master WHERE type='table' AND name='group_prompts';" | grep -q "group_prompts"; then
        echo "  → Migrating group_prompts as chat_prompts..."
        sqlite3 -header -csv "$SQLITE_DIR/spam_detection.db" "SELECT * FROM group_prompts;" > "$TEMP_DIR/chat_prompts.csv"
        local row_count=$(tail -n +2 "$TEMP_DIR/chat_prompts.csv" | wc -l | tr -d ' ')
        if [ "$row_count" -gt 0 ]; then
            psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -c "\COPY chat_prompts FROM '$TEMP_DIR/chat_prompts.csv' WITH CSV HEADER;" > /dev/null 2>&1
            echo "    ✓ Migrated $row_count rows to chat_prompts"
        else
            echo "    ⊘ No data to migrate for group_prompts"
        fi
    elif sqlite3 "$SQLITE_DIR/spam_detection.db" "SELECT name FROM sqlite_master WHERE type='table' AND name='chat_prompts';" | grep -q "chat_prompts"; then
        migrate_table "$SQLITE_DIR/spam_detection.db" "chat_prompts"
    fi

    migrate_table "$SQLITE_DIR/spam_detection.db" "spam_check_configs"
    migrate_table "$SQLITE_DIR/spam_detection.db" "spam_detection_configs"
    echo "✓ Spam detection database migration complete"
else
    echo "⚠ Warning: spam_detection.db not found at $SQLITE_DIR/spam_detection.db"
fi
echo ""

echo "Step 6: Converting SQLite boolean values (0/1) to PostgreSQL (true/false)..."
# PostgreSQL COPY accepts 0/1 but we need to ensure proper boolean conversion
psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" <<'SQL'
-- Convert 0/1 to proper booleans in all tables

-- Users table
UPDATE users SET
    is_active = CASE WHEN is_active::text = '0' THEN false ELSE true END,
    totp_enabled = CASE WHEN totp_enabled::text = '0' THEN false ELSE true END,
    email_verified = CASE WHEN email_verified::text = '0' THEN false ELSE true END
WHERE is_active::text IN ('0', '1')
   OR totp_enabled::text IN ('0', '1')
   OR email_verified::text IN ('0', '1');

-- Stop words table
UPDATE stop_words SET
    enabled = CASE WHEN enabled::text = '0' THEN false ELSE true END
WHERE enabled::text IN ('0', '1');

-- Training samples table
UPDATE training_samples SET
    is_spam = CASE WHEN is_spam::text = '0' THEN false ELSE true END
WHERE is_spam::text IN ('0', '1');

-- Spam samples table
UPDATE spam_samples SET
    enabled = CASE WHEN enabled::text = '0' THEN false ELSE true END
WHERE enabled::text IN ('0', '1');

-- Spam check configs table
UPDATE spam_check_configs SET
    enabled = CASE WHEN enabled::text = '0' THEN false ELSE true END
WHERE enabled::text IN ('0', '1');

-- Spam checks table
UPDATE spam_checks SET
    is_spam = CASE WHEN is_spam::text = '0' THEN false ELSE true END
WHERE is_spam::text IN ('0', '1');

-- Chat prompts table (if it has enabled column)
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.columns
               WHERE table_name = 'chat_prompts' AND column_name = 'enabled') THEN
        UPDATE chat_prompts SET
            enabled = CASE WHEN enabled::text = '0' THEN false ELSE true END
        WHERE enabled::text IN ('0', '1');
    END IF;
END $$;

-- Convert old audit event type values to new enum values
-- Old SQLite enum used 100s ranges, new enum uses 0-18
UPDATE audit_log SET event_type = CASE
    WHEN event_type = 100 THEN 8   -- InviteCreated
    WHEN event_type = 101 THEN 10  -- InviteRevoked
    WHEN event_type = 102 THEN 9   -- InviteUsed
    WHEN event_type = 103 THEN 17  -- UserStatusChanged
    WHEN event_type = 200 THEN 2   -- PasswordChange
    WHEN event_type = 202 THEN 13  -- PasswordReset
    WHEN event_type = 203 THEN 3   -- TotpEnabled
    WHEN event_type = 204 THEN 18  -- UserTotpDisabled
    WHEN event_type = 205 THEN 14  -- UserInviteCreated
    WHEN event_type = 206 THEN 15  -- UserInviteRevoked
    WHEN event_type = 300 THEN 0   -- Login
    WHEN event_type = 301 THEN 1   -- Logout
    WHEN event_type = 304 THEN 12  -- FailedLogin
    WHEN event_type = 400 THEN 3   -- TotpEnabled
    WHEN event_type = 401 THEN 4   -- TotpDisabled
    WHEN event_type = 500 THEN 5   -- UserCreated
    WHEN event_type = 501 THEN 6   -- UserModified
    WHEN event_type = 502 THEN 7   -- UserDeleted
    WHEN event_type = 600 THEN 11  -- PermissionChanged
    ELSE event_type  -- Keep unchanged if already in new format
END
WHERE event_type >= 100;  -- Only update old format values
SQL
echo "✓ Boolean conversion complete"
echo ""

echo "Step 7: Fixing PostgreSQL sequences..."
# Reset sequences to match current max IDs
psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" <<'SQL'
-- Reset all sequences to max(id) + 1
DO $$
DECLARE
    r RECORD;
    max_id BIGINT;
    seq_name TEXT;
BEGIN
    FOR r IN
        SELECT c.table_schema, c.table_name, c.column_name
        FROM information_schema.columns c
        WHERE c.table_schema = 'public'
          AND c.column_name = 'id'
          AND c.column_default LIKE 'nextval%'
    LOOP
        -- Get the sequence name from column default
        SELECT pg_get_serial_sequence(r.table_schema || '.' || r.table_name, r.column_name) INTO seq_name;

        -- Get max ID from table
        EXECUTE format('SELECT COALESCE(MAX(id), 0) FROM %I.%I', r.table_schema, r.table_name) INTO max_id;

        -- Set sequence to max_id (setval will increment on next call)
        IF max_id > 0 AND seq_name IS NOT NULL THEN
            EXECUTE format('SELECT setval(%L, %s)', seq_name, max_id);
            RAISE NOTICE 'Set % to % (table: %.%)', seq_name, max_id, r.table_schema, r.table_name;
        END IF;
    END LOOP;
END $$;
SQL
echo "✓ Sequences updated"
echo ""

echo "Step 8: Verifying migration..."
psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" <<'SQL'
SELECT
    'Row counts:' as info,
    (SELECT COUNT(*) FROM public.users) as users,
    (SELECT COUNT(*) FROM public.messages) as messages,
    (SELECT COUNT(*) FROM public.spam_checks) as spam_checks,
    (SELECT COUNT(*) FROM public.stop_words) as stop_words,
    (SELECT COUNT(*) FROM public.training_samples) as training_samples,
    (SELECT COUNT(*) FROM public.spam_samples) as spam_samples;
SQL
echo ""

echo "Step 9: Cleaning up temp files..."
rm -rf "$TEMP_DIR"
echo "✓ Cleanup complete"
echo ""

echo "=== Migration Complete ==="
echo ""
echo "Next steps:"
echo "  1. Test the application: dotnet run --project TelegramGroupsAdmin"
echo "  2. Backup SQLite databases: cp $SQLITE_DIR/*.db ./backups/"
echo "  3. (Optional) Remove SQLite databases after verifying PostgreSQL works"
echo ""
