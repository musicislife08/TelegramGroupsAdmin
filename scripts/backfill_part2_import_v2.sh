#!/bin/bash
set -euo pipefail

# Configuration
CONTAINER_NAME="tga-db"  # From compose/compose.yml
DB_NAME="telegram_groups_admin"
DB_USER="tgadmin"
INPUT_FILE="/tmp/enriched_messages_reviewed_v2.tsv"

echo "=== Part 2: Import Enriched Messages to Database (v2) ==="
echo ""

# Verify input file exists
if [ ! -f "$INPUT_FILE" ]; then
    echo "ERROR: Input file not found: $INPUT_FILE"
    echo "Please run backfill_part1_export_and_scrape_v2.sh first"
    exit 1
fi

record_count=$(tail -n +2 "$INPUT_FILE" | wc -l)
echo "Found $record_count enriched messages to import"
echo ""

# Step 1: Load file into staging table
echo "[1/3] Loading enriched messages into staging table..."
docker exec -i "$CONTAINER_NAME" psql -U "$DB_USER" -d "$DB_NAME" <<'SQL'
DROP TABLE IF EXISTS enriched_messages_import;
CREATE TABLE enriched_messages_import (
    message_id bigint,
    enriched_text text
);
SQL

cat "$INPUT_FILE" | docker exec -i "$CONTAINER_NAME" psql -U "$DB_USER" -d "$DB_NAME" \
  -c "COPY enriched_messages_import(message_id, enriched_text) FROM STDIN WITH (FORMAT csv, DELIMITER E'\\t', HEADER true);"

echo "   ✓ Loaded $record_count records"
echo ""

# Step 2: Preview changes (DRY RUN)
echo "[2/3] Preview of changes (first 5 messages):"
docker exec -i "$CONTAINER_NAME" psql -U "$DB_USER" -d "$DB_NAME" -x <<'SQL'
SELECT
    m.message_id,
    LENGTH(m.message_text) as old_length,
    LENGTH(e.enriched_text) as new_length,
    LENGTH(e.enriched_text) - LENGTH(m.message_text) as added_chars,
    LEFT(m.message_text, 100) as old_preview,
    CASE
        WHEN POSITION('━━━ URL Previews ━━━' IN e.enriched_text) > 0
        THEN '✓ Has URL previews section'
        ELSE '✗ Missing delimiter'
    END as validation
FROM messages m
INNER JOIN enriched_messages_import e ON m.message_id = e.message_id
ORDER BY m.message_id
LIMIT 5;
SQL
echo ""

# Step 3: Confirm before updating
read -p "Do these changes look correct? (yes/no): " confirm
if [ "$confirm" != "yes" ]; then
    echo "Aborted. No changes made to database."
    docker exec -i "$CONTAINER_NAME" psql -U "$DB_USER" -d "$DB_NAME" -c "DROP TABLE enriched_messages_import;"
    exit 0
fi
echo ""

# Step 4: Apply updates in single transaction
echo "[3/3] Applying updates to messages table..."
docker exec -i "$CONTAINER_NAME" psql -U "$DB_USER" -d "$DB_NAME" <<'SQL'
BEGIN;

UPDATE messages m
SET message_text = e.enriched_text
FROM enriched_messages_import e
WHERE m.message_id = e.message_id;

SELECT COUNT(*) as "Updated Messages" FROM enriched_messages_import;

DROP TABLE enriched_messages_import;

COMMIT;
SQL

echo ""
echo "=== Part 2 Complete ==="
echo "✓ Database updated successfully"
echo "✓ Next Bayes retraining will use enriched message text"
echo ""
