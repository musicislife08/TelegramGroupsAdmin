-- ============================================================================
-- Phase 4.19: Actor System Data Backfill
-- Migrate legacy issued_by/added_by string values to exclusive arc columns
-- ============================================================================

-- Run this script manually against the database BEFORE adding CHECK constraints

BEGIN;

-- ============================================================================
-- user_actions table (16 rows)
-- ============================================================================

-- Pattern 1: Web user GUID → web_user_id
UPDATE user_actions
SET web_user_id = issued_by
WHERE issued_by ~ '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
  AND web_user_id IS NULL;

-- Pattern 2: "Auto-Detection" → system_identifier
UPDATE user_actions
SET system_identifier = 'auto_detection'
WHERE issued_by = 'Auto-Detection'
  AND system_identifier IS NULL;

-- Pattern 3: "web_admin" → system_identifier (legacy value, treat as system)
UPDATE user_actions
SET system_identifier = 'web_admin'
WHERE issued_by = 'web_admin'
  AND system_identifier IS NULL;

-- Pattern 4: "system_bot_protection" → system_identifier
UPDATE user_actions
SET system_identifier = 'bot_protection'
WHERE issued_by = 'system_bot_protection'
  AND system_identifier IS NULL;

-- Pattern 5: NULL or empty → system_identifier = 'unknown'
UPDATE user_actions
SET system_identifier = 'unknown'
WHERE (issued_by IS NULL OR issued_by = '')
  AND web_user_id IS NULL
  AND telegram_user_id IS NULL
  AND system_identifier IS NULL;

-- ============================================================================
-- detection_results table (275 rows)
-- ============================================================================

-- Pattern 1: Web user GUID → web_user_id
UPDATE detection_results
SET web_user_id = added_by
WHERE added_by ~ '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
  AND web_user_id IS NULL;

-- Pattern 2: NULL or empty → system_identifier = 'auto_detection' (spam detection system)
UPDATE detection_results
SET system_identifier = 'auto_detection'
WHERE (added_by IS NULL OR added_by = '')
  AND web_user_id IS NULL
  AND telegram_user_id IS NULL
  AND system_identifier IS NULL;

-- ============================================================================
-- stop_words table (11 rows)
-- ============================================================================

-- Pattern 1: Web user GUID → web_user_id
UPDATE stop_words
SET web_user_id = added_by
WHERE added_by ~ '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
  AND web_user_id IS NULL;

-- Pattern 2: NULL or empty → system_identifier = 'initial_seed'
UPDATE stop_words
SET system_identifier = 'initial_seed'
WHERE (added_by IS NULL OR added_by = '')
  AND web_user_id IS NULL
  AND telegram_user_id IS NULL
  AND system_identifier IS NULL;

-- ============================================================================
-- admin_notes table (0 rows) - Nothing to backfill
-- ============================================================================

-- ============================================================================
-- user_tags table (0 rows) - Nothing to backfill
-- ============================================================================

-- ============================================================================
-- Verification: Ensure all rows have exactly one actor column populated
-- ============================================================================

DO $$
DECLARE
    invalid_count INTEGER;
BEGIN
    -- Check user_actions
    SELECT COUNT(*) INTO invalid_count
    FROM user_actions
    WHERE (web_user_id IS NOT NULL)::int + (telegram_user_id IS NOT NULL)::int + (system_identifier IS NOT NULL)::int != 1;

    IF invalid_count > 0 THEN
        RAISE EXCEPTION 'user_actions has % rows with invalid actor configuration', invalid_count;
    END IF;

    -- Check detection_results
    SELECT COUNT(*) INTO invalid_count
    FROM detection_results
    WHERE (web_user_id IS NOT NULL)::int + (telegram_user_id IS NOT NULL)::int + (system_identifier IS NOT NULL)::int != 1;

    IF invalid_count > 0 THEN
        RAISE EXCEPTION 'detection_results has % rows with invalid actor configuration', invalid_count;
    END IF;

    -- Check stop_words
    SELECT COUNT(*) INTO invalid_count
    FROM stop_words
    WHERE (web_user_id IS NOT NULL)::int + (telegram_user_id IS NOT NULL)::int + (system_identifier IS NOT NULL)::int != 1;

    IF invalid_count > 0 THEN
        RAISE EXCEPTION 'stop_words has % rows with invalid actor configuration', invalid_count;
    END IF;

    RAISE NOTICE 'Backfill verification successful! All rows have exactly one actor.';
END $$;

-- ============================================================================
-- Summary Report
-- ============================================================================

SELECT 'user_actions' as table_name,
       COUNT(*) FILTER (WHERE web_user_id IS NOT NULL) as web_users,
       COUNT(*) FILTER (WHERE telegram_user_id IS NOT NULL) as telegram_users,
       COUNT(*) FILTER (WHERE system_identifier IS NOT NULL) as system_actions,
       COUNT(*) as total
FROM user_actions
UNION ALL
SELECT 'detection_results',
       COUNT(*) FILTER (WHERE web_user_id IS NOT NULL),
       COUNT(*) FILTER (WHERE telegram_user_id IS NOT NULL),
       COUNT(*) FILTER (WHERE system_identifier IS NOT NULL),
       COUNT(*)
FROM detection_results
UNION ALL
SELECT 'stop_words',
       COUNT(*) FILTER (WHERE web_user_id IS NOT NULL),
       COUNT(*) FILTER (WHERE telegram_user_id IS NOT NULL),
       COUNT(*) FILTER (WHERE system_identifier IS NOT NULL),
       COUNT(*)
FROM stop_words;

-- If everything looks good, commit the transaction
COMMIT;

-- ============================================================================
-- Next Steps After Running This Script:
-- ============================================================================
-- 1. Verify the summary report shows expected actor distribution
-- 2. Uncomment CHECK constraints in AppDbContext.cs
-- 3. Generate new migration: dotnet ef migrations add AddActorSystemCheckConstraints
-- 4. Apply migration: dotnet run --migrate-only
-- ============================================================================
