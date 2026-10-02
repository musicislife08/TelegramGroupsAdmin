-- One-off conversion of canonical detection_results to verdict events (canonical edit 2026-09-27).
-- Deliberately independent of the AddVerdictEvents migration; checked by CanonicalVerdictOracleTests.
-- Provenance only: not embedded, not run by tests. The committed 32_detection_results.sql is its output.
-- Input: the pre-conversion canonical files (commit 6694ab1d); it is not re-runnable on converted canonical.
--
-- How it was run (scratch DB only; never against telegram_groups_admin):
--   docker exec tga-db createdb -U tgadmin canonical_convert
--   ASPNETCORE_ENVIRONMENT=Development \
--   ConnectionStrings__PostgreSQL="Host=localhost;Port=5432;Database=canonical_convert;Username=tgadmin;Password=changeme" \
--   App__DataPath=<scratch dir> SKIP_ML_TRAINING=true DYLD_LIBRARY_PATH=/opt/homebrew/lib \
--     dotnet run --no-launch-profile --project TelegramGroupsAdmin -- --migrate-only
--   (--no-launch-profile is required: the launch profile's ConnectionStrings__PostgreSQL overrides the shell's.)
--   docker exec tga-db psql -U tgadmin -d canonical_convert -c "TRUNCATE ban_celebration_captions RESTART IDENTITY"
--   (startup seeds default captions, which collide with 06_ban_celebration_captions.sql)
--   for f in $(ls TelegramGroupsAdmin.Testing.Golden/SQL/canonical/*.sql | sort); do
--     docker exec -i tga-db psql -q -v ON_ERROR_STOP=1 -U tgadmin -d canonical_convert < "$f"; done
--   docker exec -i tga-db psql -v ON_ERROR_STOP=1 -U tgadmin -d canonical_convert < <this file>
--   docker exec tga-db pg_dump -U tgadmin -d canonical_convert --data-only --column-inserts -t detection_results \
--     | grep -E '^(INSERT INTO|SELECT pg_catalog.setval)' \
--     | sed -E "s/^INSERT INTO public\./INSERT INTO /; s/setval\('public\./setval('/" > .../canonical/32_detection_results.sql
--   docker exec tga-db dropdb -U tgadmin canonical_convert
-- The training_labels edits at the bottom are mirrored by hand in 33_training_labels.sql.
BEGIN;

UPDATE detection_results SET source = CASE
    WHEN detection_source = 'auto' THEN 0
    WHEN detection_source = 'file_scan' THEN 1
    WHEN detection_source = 'tg-spam-import' THEN 18
    WHEN chat_id = 0 THEN 16
    WHEN reason LIKE 'Manually marked as spam by admin via UI%' THEN 11
    WHEN reason LIKE 'Manually marked as ham%' OR reason LIKE 'Manually added as ham training sample%' THEN 12
    WHEN reason LIKE 'Spam detected via /spam%' THEN 13
    WHEN reason LIKE 'Report #%' THEN 14
    ELSE 99 END;

-- canonical scans: no sub-threshold or AI-review rows exist, so the sign + training flag is exact
UPDATE detection_results SET classification = CASE
    WHEN net_score > 0 AND used_for_training THEN 2
    WHEN net_score > 0 THEN 4
    ELSE 3 END
WHERE source = 0;

UPDATE detection_results SET classification = CASE
    WHEN reason ILIKE '%label - ham%' THEN 1 ELSE 0 END
WHERE source = 18;

UPDATE detection_results SET classification = CASE
    WHEN source IN (10, 11, 13, 14) THEN 0
    WHEN source = 12 THEN 1
    WHEN net_score > 0 THEN 0 ELSE 1 END
WHERE source NOT IN (0, 1, 18);

INSERT INTO detection_results (message_id, chat_id, detected_at, detection_source, detection_method, score, net_score,
    reason, web_user_id, telegram_user_id, system_identifier, used_for_training, edit_version, source, classification, properties)
SELECT message_id, chat_id, detected_at + interval '1 microsecond', detection_source, 'TrainingExclude', score, net_score,
    'Removed from training', web_user_id, telegram_user_id, system_identifier, false, 0, 17,
    CASE WHEN classification = 0 THEN 4 ELSE 5 END, NULL
FROM detection_results WHERE source IN (16, 18) AND used_for_training = false
ORDER BY chat_id, message_id;

INSERT INTO detection_results (message_id, chat_id, detected_at, detection_source, detection_method, score, net_score,
    reason, telegram_user_id, system_identifier, used_for_training, edit_version, source, classification, audit_log_id)
SELECT tl.message_id, tl.chat_id, tl.labeled_at, 'manual',
    CASE WHEN tl.label = 0 AND tl.labeled_by_user_id IS NULL THEN 'AutoBan' ELSE 'Manual' END,
    5.0, CASE WHEN tl.label = 0 THEN 5.0 ELSE -5.0 END, COALESCE(tl.reason, 'Training label'),
    tl.labeled_by_user_id,
    CASE WHEN tl.labeled_by_user_id IS NULL THEN CASE WHEN tl.label = 0 THEN 'auto_detection' ELSE 'unknown' END END,
    false, 0,
    CASE WHEN tl.label = 0 AND tl.labeled_by_user_id IS NULL THEN 10 ELSE 99 END,
    tl.label::int, tl.audit_log_id
FROM training_labels tl
WHERE NOT EXISTS (SELECT 1 FROM detection_results d
                  WHERE d.chat_id = tl.chat_id AND d.message_id = tl.message_id
                    AND d.source NOT IN (0, 1, 17) AND (d.classification = 0) = (tl.label = 0))
ORDER BY tl.chat_id, tl.message_id;

-- ---------------------------------------------------------------------------------------------
-- Flag-edits (canonical edit 2026-09-27). Each anchor was unreferenced by tests/docs beforehand.
-- ---------------------------------------------------------------------------------------------

-- 4a. edit rescan flips a message to spam (msg 82837 @financerope): the admin marked it ham,
-- the user then edited it into spam, and the newer v1 rescan wins. The admin ham decision (dr 1343)
-- and its training label are re-timed to 21:59, after scan dr 1333 (21:57) and before the first edit (22:00).
UPDATE detection_results SET detected_at = '2025-10-29 21:59:00+00' WHERE id = 1343;
UPDATE detection_results SET score = 4.5, net_score = 4.5, used_for_training = true, classification = 2,
    reason = '[Edit #1] AI confirmed spam: AI: Spam - promotional contact request (score: 4.5)'
WHERE id = 1334;

-- 4b. spam among @mouthsafeguard's last three (msg 7796): an untrained spam scan, no admin label
UPDATE detection_results SET score = 3.0, net_score = 3.0, used_for_training = false, classification = 4,
    reason = 'AI confirmed spam: AI: Spam - unsolicited offer (score: 3.0)'
WHERE id = 1339;
DELETE FROM detection_results WHERE message_id = 7796 AND source IN (12, 99) AND classification = 1;

-- 4c. an attachment scan beside a content scan (msg 216684): the manual row becomes a clean file scan
UPDATE detection_results SET detection_source = 'file_scan', detection_method = 'FileScanningCheck',
    source = 1, classification = 5, score = 0, net_score = 0, used_for_training = false,
    reason = 'No threats detected', web_user_id = NULL, telegram_user_id = NULL,
    system_identifier = 'file_scanner', check_results_json = NULL,
    detected_at = (SELECT max(detected_at) + interval '1 minute' FROM detection_results WHERE message_id = 216684)
WHERE id = 2535;

-- 4d. UntrainedHam anchor (msg 22160, Crypto Group, sender 9621984255379): a candidate of the brief's query
-- (auto scan with an OpenAI check, no training label, exactly one detection row), unreferenced, and the only
-- one whose message was kept and whose sender is not banned. AI review 2.0 is below the review threshold,
-- so the verdict is UntrainedHam (allowed, never trained).
UPDATE detection_results SET score = 2.0, net_score = 2.0, used_for_training = false, classification = 5,
    reason = 'AI below review threshold: AI: Review - friendly greeting, could be a lead-in (score: 2.0)',
    check_results_json = '{"Checks":[{"CheckName":2,"Score":3.5,"IsSpam":true,"Details":"High similarity to spam sample","Abstained":false,"ProcessingTimeMs":1.2},{"CheckName":6,"Score":2.0,"IsSpam":true,"Details":"AI: Review - friendly greeting, could be a lead-in","Abstained":false,"ProcessingTimeMs":2100.0}]}'
WHERE id = 1933;

-- training_labels edits (mirrored by hand in 33_training_labels.sql)
UPDATE training_labels SET labeled_at = '2025-10-29 21:59:00+00' WHERE chat_id = -100065252085265 AND message_id = 82837;
DELETE FROM training_labels WHERE chat_id = -100017312732389 AND message_id = 7796;
DELETE FROM training_labels WHERE chat_id = -100026957614982 AND message_id = 216684;

COMMIT;
