-- Analytics Test Data for Integration Tests
-- References existing messages from 04_base_messages.sql (IDs: 82581, 82594, 82596, 82603, 82606, 82612, 82615-82619)
-- References existing users from 00_base_telegram_users.sql
-- References existing chats from 02_base_managed_chats.sql
--
-- MUST be loaded after base data scripts (00-05)
-- is_spam is computed from net_confidence > 0 (positive = spam, zero/negative = ham)
--
-- IMPORTANT: All timestamps use date_trunc('day', NOW()) to anchor to midnight UTC.
-- This prevents records from drifting across day boundaries when tests run near midnight.
-- "Today" records use midnight + small offset (always within today).
-- "Yesterday" records use midnight - 1 day + offset (always within yesterday).

-- ============================================================
-- 1. SPAM DETECTION RESULTS
-- Base data (05_base_detection_results.sql) only has ham detections
-- We need spam (net_confidence > 0) for trend testing
-- ============================================================

INSERT INTO detection_results (message_id, detected_at, detection_source, detection_method, confidence, reason, system_identifier, used_for_training, net_confidence, check_results_json, edit_version)
VALUES
-- Today's spam (3 records) - use messages 82617, 82618, 82616
-- Anchored to today's midnight + small offsets (safe even at 00:00:01 UTC)
(82617, date_trunc('day', NOW()) + INTERVAL '3 seconds', 'System', 'StopWords, Bayes', 85, 'Spam detected: multiple triggers',
 'spam-detection-v1', FALSE, 85,
 '{{"Checks":[{{"CheckName":0,"Result":1,"Confidence":85,"ProcessingTimeMs":5.2}},{{"CheckName":3,"Result":1,"Confidence":78,"ProcessingTimeMs":12.1}}]}}',
 0),
(82618, date_trunc('day', NOW()) + INTERVAL '2 seconds', 'System', 'StopWords', 72, 'Stop word match detected',
 'spam-detection-v1', FALSE, 72,
 '{{"Checks":[{{"CheckName":0,"Result":1,"Confidence":72,"ProcessingTimeMs":3.8}}]}}',
 0),
(82616, date_trunc('day', NOW()) + INTERVAL '1 second', 'System', 'OpenAI', 90, 'LLM flagged as spam content',
 'spam-detection-v1', FALSE, 90,
 '{{"Checks":[{{"CheckName":6,"Result":1,"Confidence":90,"ProcessingTimeMs":1250}}]}}',
 0),

-- Yesterday's spam (2 records) - use messages 82615, 82612
-- Anchored to yesterday's midnight + offset (always within yesterday)
(82615, date_trunc('day', NOW()) - INTERVAL '1 day' + INTERVAL '12 hours', 'System', 'Bayes', 68, 'Bayes classifier flagged',
 'spam-detection-v1', FALSE, 68,
 '{{"Checks":[{{"CheckName":3,"Result":1,"Confidence":68,"ProcessingTimeMs":8.5}}]}}',
 0),
(82612, date_trunc('day', NOW()) - INTERVAL '1 day' + INTERVAL '10 hours', 'System', 'StopWords', 75, 'Keyword pattern match',
 'spam-detection-v1', FALSE, 75,
 NULL,
 0),

-- Last week's spam (2 records) - use messages 82606, 82603
(82606, date_trunc('day', NOW()) - INTERVAL '8 days' + INTERVAL '12 hours', 'System', 'StopWords', 80, 'Spam pattern detected',
 'spam-detection-v1', FALSE, 80,
 NULL,
 0),
(82603, date_trunc('day', NOW()) - INTERVAL '9 days' + INTERVAL '12 hours', 'System', 'Bayes', 65, 'Spam probability exceeded threshold',
 'spam-detection-v1', FALSE, 65,
 NULL,
 0);


-- ============================================================
-- 2. MANUAL CORRECTIONS (for FP/FN accuracy tests)
-- These create correction records that override automated detections
-- ============================================================

-- False Positive correction: Message 82617 was flagged spam above, now corrected to ham
-- (detection_source='manual', net_confidence negative = ham)
INSERT INTO detection_results (message_id, detected_at, detection_source, detection_method, confidence, reason, system_identifier, used_for_training, net_confidence, edit_version)
VALUES
(82617, date_trunc('day', NOW()) + INTERVAL '4 seconds', 'manual', 'Manual Review', 100, 'Admin correction: not spam (false positive)', 'manual-review', FALSE, -100, 0);

-- First add a ham detection for message 82594 (required for FN test - needs original automated detection)
INSERT INTO detection_results (message_id, detected_at, detection_source, detection_method, confidence, reason, system_identifier, used_for_training, net_confidence, edit_version)
VALUES
(82594, date_trunc('day', NOW()) + INTERVAL '5 seconds', 'System', 'StopWords, Bayes', 0, 'No spam detected', 'spam-detection-v1', FALSE, 0, 0);

-- False Negative correction: Message 82594 was detected as ham above, now corrected to spam
-- (detection_source='manual', net_confidence positive = spam)
INSERT INTO detection_results (message_id, detected_at, detection_source, detection_method, confidence, reason, system_identifier, used_for_training, net_confidence, edit_version)
VALUES
(82594, date_trunc('day', NOW()) + INTERVAL '6 seconds', 'manual', 'Manual Review', 100, 'Admin correction: is spam (false negative)', 'manual-review', FALSE, 100, 0);


-- ============================================================
-- 3. WELCOME RESPONSES (not in any base script)
-- Use existing chat_id (-1001322973935) and user_ids from base data
-- WelcomeResponseType: Pending=0, Accepted=1, Denied=2, Timeout=3, Left=4
-- ============================================================

INSERT INTO welcome_responses (chat_id, user_id, username, welcome_message_id, response, responded_at, dm_sent, dm_fallback, created_at)
VALUES
-- Today: 2 accepted, 1 denied (anchored to today's midnight + offsets)
(-1001322973935, 100001, 'alice_user', 1001, 1, date_trunc('day', NOW()) + INTERVAL '3 seconds', TRUE, FALSE, date_trunc('day', NOW()) + INTERVAL '1 second'),
(-1001322973935, 100002, 'bob_chat', 1002, 1, date_trunc('day', NOW()) + INTERVAL '4 seconds', TRUE, FALSE, date_trunc('day', NOW()) + INTERVAL '2 seconds'),
(-1001322973935, 100003, 'charlie_msg', 1003, 2, date_trunc('day', NOW()) + INTERVAL '5 seconds', FALSE, FALSE, date_trunc('day', NOW()) + INTERVAL '3 seconds'),

-- Yesterday: 1 timeout, 1 left (anchored to yesterday's midnight + offsets)
(-1001322973935, 100004, 'diana_test', 1004, 3, date_trunc('day', NOW()) - INTERVAL '1 day' + INTERVAL '12 hours', FALSE, FALSE, date_trunc('day', NOW()) - INTERVAL '1 day' + INTERVAL '12 hours'),
(-1001322973935, 100005, 'eve_trusted', 1005, 4, date_trunc('day', NOW()) - INTERVAL '1 day' + INTERVAL '10 hours', FALSE, FALSE, date_trunc('day', NOW()) - INTERVAL '1 day' + INTERVAL '10 hours'),

-- Last week (for trends)
(-1001322973935, 100006, NULL, 1006, 1, date_trunc('day', NOW()) - INTERVAL '7 days' + INTERVAL '12 hours 2 minutes', TRUE, FALSE, date_trunc('day', NOW()) - INTERVAL '7 days' + INTERVAL '12 hours');
