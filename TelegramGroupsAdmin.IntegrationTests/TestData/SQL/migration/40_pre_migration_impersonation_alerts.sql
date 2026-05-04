-- Pre-Migration Impersonation Alerts for Testing Data Migration
-- This file contains impersonation_alerts data in the OLD schema format
-- (before UnifiedReviewsAndExamSessions migration converts them to unified reports)
--
-- IMPORTANT: This script is designed to run AFTER migrations up to 20260114023351_AddEnrichedMessagesView
-- but BEFORE 20260117003553_UnifiedReviewsAndExamSessions
--
-- FK Dependencies:
--   - suspected_user_id → telegram_users.telegram_user_id (use IDs from 00_base_telegram_users.sql)
--   - target_user_id → telegram_users.telegram_user_id (use IDs from 00_base_telegram_users.sql)
--   - reviewed_by_user_id → users.id (use IDs from 01_base_web_users.sql, nullable)
--
-- Test Scenarios Covered:
--   1. Fully reviewed alert with all fields populated (Critical risk, auto-banned, confirmed scam)
--   2. Pending alert (not reviewed, no verdict)
--   3. Reviewed alert with FalsePositive verdict (whitelisted user)
--   4. Medium risk alert with only name match (no photo)
--
-- Expected Migration Results:
--   - All records should migrate to reports table with type=1
--   - status should be 0 (Pending) if reviewed_at IS NULL, else 1 (Reviewed)
--   - JSONB context should contain all impersonation-specific fields
--   - risk_level int should map to string: 0=low, 1=medium, 2=high, 3=critical
--   - verdict int should map to string: 0=false_positive, 1=confirmed_scam, 2=whitelisted

INSERT INTO impersonation_alerts (
    chat_id,
    suspected_user_id,
    target_user_id,
    name_match,
    photo_match,
    photo_similarity_score,
    total_score,
    risk_level,
    auto_banned,
    detected_at,
    reviewed_at,
    reviewed_by_user_id,
    verdict
) VALUES
-- Alert 1: Critical risk, fully reviewed, confirmed scam, auto-banned
-- Suspected user (100001/alice) impersonating admin (100003/charlie)
(
    -1001234567890,                                     -- chat_id (test group)
    100001,                                             -- suspected_user_id (alice_user)
    100003,                                             -- target_user_id (charlie_msg - trusted admin)
    true,                                               -- name_match
    true,                                               -- photo_match
    0.95,                                               -- photo_similarity_score (95% match)
    100,                                                -- total_score (max: both name and photo match)
    3,                                                  -- risk_level = 3 (Critical)
    true,                                               -- auto_banned (was auto-banned due to high score)
    '2025-12-15 10:30:00+00'::timestamptz,             -- detected_at
    '2025-12-15 11:00:00+00'::timestamptz,             -- reviewed_at (30 min later)
    'b388ee38-0ed3-4c09-9def-5715f9f07f56',            -- reviewed_by_user_id (owner@example.com)
    1                                                   -- verdict = 1 (ConfirmedScam)
),
-- Alert 2: Medium risk, pending review (not yet reviewed)
-- Suspected user (100002/bob) possible impersonation
(
    -1001234567890,                                     -- same chat
    100002,                                             -- suspected_user_id (bob_chat)
    100004,                                             -- target_user_id (diana_test)
    true,                                               -- name_match only
    false,                                              -- no photo_match
    null,                                               -- no photo similarity (no photo match)
    50,                                                 -- total_score (name only = 50)
    1,                                                  -- risk_level = 1 (Medium)
    false,                                              -- not auto_banned (score too low)
    '2025-12-16 09:00:00+00'::timestamptz,             -- detected_at
    null,                                               -- NOT reviewed yet
    null,                                               -- no reviewer
    null                                                -- no verdict
),
-- Alert 3: Low risk, reviewed as false positive
-- User (100005/eve) flagged but cleared
(
    -1009876543210,                                     -- different chat
    100005,                                             -- suspected_user_id (eve_trusted)
    100006,                                             -- target_user_id (Frank)
    false,                                              -- no name_match
    true,                                               -- photo_match (similar avatar)
    0.82,                                               -- photo_similarity_score (82% - borderline)
    50,                                                 -- total_score
    0,                                                  -- risk_level = 0 (Low)
    false,                                              -- not auto_banned
    '2025-12-17 14:00:00+00'::timestamptz,             -- detected_at
    '2025-12-17 15:30:00+00'::timestamptz,             -- reviewed_at
    '921637d5-0f65-4c66-b143-6f057dd06a1c',            -- reviewed_by_user_id (admin@example.com)
    0                                                   -- verdict = 0 (FalsePositive)
),
-- Alert 4: High risk, reviewed and whitelisted
-- User (100007/grace) added to whitelist after review
(
    -1001234567890,                                     -- back to first chat
    100007,                                             -- suspected_user_id (grace_j)
    100003,                                             -- target_user_id (charlie_msg)
    true,                                               -- name_match
    false,                                              -- no photo_match
    null,                                               -- no photo similarity
    50,                                                 -- total_score (name only)
    2,                                                  -- risk_level = 2 (High)
    false,                                              -- not auto_banned
    '2025-12-18 08:00:00+00'::timestamptz,             -- detected_at
    '2025-12-18 08:15:00+00'::timestamptz,             -- reviewed_at (quick review)
    'b388ee38-0ed3-4c09-9def-5715f9f07f56',            -- reviewed_by_user_id (owner@example.com)
    2                                                   -- verdict = 2 (Whitelisted)
);
