-- Old messages test data: Messages with various ages for testing retention/cleanup logic
-- Message IDs: 96001-96006 (to avoid conflicts with ML training data 90001-90040 and dedup 95001-95022)
-- Test scenarios:
--   1. Old message, no detection results -> DELETED
--   2. Old message, detection result with used_for_training=true -> PRESERVED (training data)
--   3. Old message, detection result with used_for_training=false -> DELETED (cascade deletes detection)
--   4. Boundary case: exactly 30 days old -> PRESERVED (on boundary)

-- Old message 1: 45 days old, no detection results -> should be deleted
INSERT INTO messages (message_id, chat_id, user_id, message_text, timestamp)
VALUES (
    96001,
    -1001322973935,  -- MainChat_Id
    100001,          -- alice_user
    'Old message that should be cleaned up',
    NOW() - INTERVAL '45 days'
);

-- Old message 2: 60 days old, no detection results -> should be deleted
INSERT INTO messages (message_id, chat_id, user_id, message_text, timestamp)
VALUES (
    96002,
    -1001322973935,  -- MainChat_Id
    100002,          -- bob_chat
    'Another old message for cleanup testing',
    NOW() - INTERVAL '60 days'
);

-- Old message 3: 90 days old WITH training data (used_for_training=true) -> PRESERVED
INSERT INTO messages (message_id, chat_id, user_id, message_text, timestamp)
VALUES (
    96003,
    -1001322973935,  -- MainChat_Id
    100003,          -- charlie_msg
    'Old message with training data - keep for ML',
    NOW() - INTERVAL '90 days'
);

-- Detection result for message 96003 (training data - preserved)
INSERT INTO detection_results (message_id, detected_at, detection_source, detection_method, confidence, net_confidence, used_for_training, system_identifier, edit_version)
VALUES (
    96003,
    NOW() - INTERVAL '90 days',
    'TextClassifier',
    'Bayes',
    95,
    95,
    true,  -- Training data - message will be preserved
    'CleanupTest',
    0  -- Original message (not an edit)
);

-- Old message 4: 35 days old (just past threshold) -> should be deleted
INSERT INTO messages (message_id, chat_id, user_id, message_text, timestamp)
VALUES (
    96004,
    -1001322973935,  -- MainChat_Id
    100004,          -- diana_test
    'Edge case: just past 30 day retention',
    NOW() - INTERVAL '35 days'
);

-- Boundary message: 29 days old -> should NOT be deleted (just inside retention)
INSERT INTO messages (message_id, chat_id, user_id, message_text, timestamp)
VALUES (
    96005,
    -1001322973935,  -- MainChat_Id
    100005,          -- eve_trusted
    'Boundary case: 29 days old - just inside retention',
    NOW() - INTERVAL '29 days'
);

-- Old message 5: 50 days old WITH detection result but used_for_training=false -> DELETED
-- This tests that non-training detection results don't prevent cleanup
INSERT INTO messages (message_id, chat_id, user_id, message_text, timestamp)
VALUES (
    96006,
    -1001322973935,  -- MainChat_Id
    100006,
    'Old message with non-training detection result - should be deleted',
    NOW() - INTERVAL '50 days'
);

-- Detection result for message 96006 (NOT training data - will cascade delete with message)
INSERT INTO detection_results (message_id, detected_at, detection_source, detection_method, confidence, net_confidence, used_for_training, system_identifier, edit_version)
VALUES (
    96006,
    NOW() - INTERVAL '50 days',
    'TextClassifier',
    'Bayes',
    80,
    80,
    false,  -- NOT training data - will cascade delete when message deleted
    'CleanupTest',
    0  -- Original message (not an edit)
);

-- ============================================================================
-- Related data for cascade delete testing
-- ============================================================================

-- Edit for message 96001 (cascade deletes when message is deleted)
-- Edit IDs: 960001+ to avoid conflicts
INSERT INTO message_edits (id, message_id, edit_date, old_text, new_text)
VALUES (
    960001,
    96001,
    NOW() - INTERVAL '44 days',
    'Original text before edit',
    'Old message that should be cleaned up'
);

-- Translation for message 96002 (cascade deletes when message is deleted)
INSERT INTO message_translations (message_id, translated_text, detected_language, translated_at)
VALUES (
    96002,
    'Another old message for cleanup testing (translated)',
    'es',
    NOW() - INTERVAL '60 days'
);

-- Translation for an edit (cascade deletes: message -> edit -> translation)
INSERT INTO message_translations (edit_id, translated_text, detected_language, translated_at)
VALUES (
    960001,
    'Old message that should be cleaned up (translated edit)',
    'fr',
    NOW() - INTERVAL '44 days'
);
