-- Base Telegram Users for Integration Tests
-- Contains 8 users including system user (ID 0)
-- User IDs: 0 (system), 100001-100007 (test users)
-- NO FK dependencies - can be loaded first

INSERT INTO telegram_users (telegram_user_id, username, first_name, last_name, is_trusted, bot_dm_enabled, first_seen_at, last_seen_at, created_at, updated_at)
VALUES
-- System user (special ID 0)
(0, 'system', NULL, NULL, FALSE, FALSE, NOW() - INTERVAL '14 days', NOW(), NOW() - INTERVAL '14 days', NOW()),
-- User1: alice_user (bot DM enabled)
(100001, 'alice_user', 'Alice', 'Anderson', FALSE, TRUE, NOW() - INTERVAL '13 days', NOW(), NOW() - INTERVAL '13 days', NOW()),
-- User2: bob_chat
(100002, 'bob_chat', 'Bob', 'Brown', FALSE, FALSE, NOW() - INTERVAL '12 days', NOW(), NOW() - INTERVAL '12 days', NOW()),
-- User3: charlie_msg (trusted)
(100003, 'charlie_msg', 'Charlie', 'Clark', TRUE, FALSE, NOW() - INTERVAL '11 days', NOW(), NOW() - INTERVAL '11 days', NOW()),
-- User4: diana_test (bot DM enabled)
(100004, 'diana_test', 'Diana', 'Davis', FALSE, TRUE, NOW() - INTERVAL '10 days', NOW(), NOW() - INTERVAL '10 days', NOW()),
-- User5: eve_trusted (no first name, trusted)
(100005, 'eve_trusted', NULL, NULL, TRUE, FALSE, NOW() - INTERVAL '9 days', NOW(), NOW() - INTERVAL '9 days', NOW()),
-- User6: no username, just first name (trusted)
(100006, NULL, 'Frank', NULL, TRUE, FALSE, NOW() - INTERVAL '8 days', NOW(), NOW() - INTERVAL '8 days', NOW()),
-- User7: grace_j (trusted)
(100007, 'grace_j', NULL, NULL, TRUE, FALSE, NOW() - INTERVAL '7 days', NOW(), NOW() - INTERVAL '7 days', NOW()),
-- Additional users referenced by messages in 11_training_full.sql and other training scripts
-- These user IDs appear in existing training data to avoid FK violations
(549755813888, 'training_user_1', 'Training', 'User', FALSE, FALSE, NOW() - INTERVAL '6 days', NOW(), NOW() - INTERVAL '6 days', NOW()),
(1232994248, 'additional_user_1', 'Additional', 'One', FALSE, FALSE, NOW() - INTERVAL '5 days', NOW(), NOW() - INTERVAL '5 days', NOW()),
(934156131, 'additional_user_2', 'Additional', 'Two', FALSE, FALSE, NOW() - INTERVAL '5 days', NOW(), NOW() - INTERVAL '5 days', NOW()),
(468009795, 'additional_user_3', 'Additional', 'Three', FALSE, FALSE, NOW() - INTERVAL '5 days', NOW(), NOW() - INTERVAL '5 days', NOW());
