-- Minimal Training Labels for Integration Tests
-- Contains 5 labels from GoldenDataset: 3 spam + 2 ham
-- FK: message_id references messages.message_id
-- FK: labeled_by_user_id references telegram_users.telegram_user_id (nullable)
-- MUST be loaded after 04_base_messages.sql and 00_base_telegram_users.sql
-- This is BELOW the ML.NET SDCA minimum threshold (20 per class)
-- Use for tests validating behavior with insufficient training data

INSERT INTO training_labels (message_id, label, labeled_by_user_id, labeled_at, reason, audit_log_id)
VALUES
-- Label1: Msg1 (82619) marked as spam by User1 (alice_user)
(82619, 0, 100001, NOW() - INTERVAL '1 day', 'Manual spam marking via /report command', NULL),
-- Label2: Msg2 (82618) marked as spam (no user attribution)
(82618, 0, NULL, NOW() - INTERVAL '2 days', 'Confirmed spam pattern', NULL),
-- Label3: Msg3 (82617) corrected to ham by User2 (bob_chat)
(82617, 1, 100002, NOW() - INTERVAL '3 days', 'Admin correction - false positive', NULL),
-- Label4: Msg4 (82616) marked as ham (no reason, no user)
(82616, 1, NULL, NOW() - INTERVAL '4 days', NULL, NULL),
-- Label5: Msg5 (82615) marked as spam (no reason, no user)
(82615, 0, NULL, NOW() - INTERVAL '5 days', NULL, NULL);
