-- Analytics Test Data for Integration Tests
-- References existing messages from 04_base_messages.sql (IDs: 82581, 82594, 82596, 82603, 82606, 82612, 82615-82619)
-- References existing users from 00_base_telegram_users.sql
-- References existing chats from 02_base_managed_chats.sql
--
-- MUST be loaded after base data scripts (00-05)
-- is_spam is computed from net_score > 0 (positive = spam, zero/negative = ham)
--
-- IMPORTANT: All timestamps use date_trunc('day', NOW()) to anchor to midnight UTC.
-- This prevents records from drifting across day boundaries when tests run near midnight.
-- "Today" records use midnight + small offset (always within today).
-- "Yesterday" records use midnight - 1 day + offset (always within yesterday).

-- ============================================================
-- 1. SPAM DETECTION RESULTS
-- Base data (05_base_detection_results.sql) only has ham detections
-- We need spam (net_score > 0) for trend testing
-- ============================================================

INSERT INTO detection_results (message_id, chat_id, detected_at, detection_source, detection_method, score, reason, system_identifier, used_for_training, net_score, check_results_json, edit_version)
VALUES
-- Today's spam (3 records) - use messages 82617, 82618, 82616
-- Anchored to today's midnight + small offsets (safe even at 00:00:01 UTC)
(82617, -1001322973935, date_trunc('day', NOW()) + INTERVAL '3 seconds', 'System', 'StopWords, Bayes', 4.25, 'Spam detected: multiple triggers',
 'spam-detection-v1', FALSE, 4.25,
 '{{"Checks":[{{"CheckName":0,"Score":4.25,"Abstained":false,"Details":"StopWords triggered","ProcessingTimeMs":5.2}},{{"CheckName":3,"Score":3.9,"Abstained":false,"Details":"Bayes flagged","ProcessingTimeMs":12.1}}]}}',
 0),
(82618, -1001322973935, date_trunc('day', NOW()) + INTERVAL '2 seconds', 'System', 'StopWords', 3.6, 'Stop word match detected',
 'spam-detection-v1', FALSE, 3.6,
 '{{"Checks":[{{"CheckName":0,"Score":3.6,"Abstained":false,"Details":"Stop word match detected","ProcessingTimeMs":3.8}}]}}',
 0),
(82616, -1001322973935, date_trunc('day', NOW()) + INTERVAL '1 second', 'System', 'OpenAI', 4.5, 'LLM flagged as spam content',
 'spam-detection-v1', FALSE, 4.5,
 '{{"Checks":[{{"CheckName":6,"Score":4.5,"Abstained":false,"Details":"LLM flagged as spam content","ProcessingTimeMs":1250}}]}}',
 0),

-- Yesterday's spam (2 records) - use messages 82615, 82612
-- Anchored to yesterday's midnight + offset (always within yesterday)
(82615, -1001322973935, date_trunc('day', NOW()) - INTERVAL '1 day' + INTERVAL '12 hours', 'System', 'Bayes', 3.4, 'Bayes classifier flagged',
 'spam-detection-v1', FALSE, 3.4,
 '{{"Checks":[{{"CheckName":3,"Score":3.4,"Abstained":false,"Details":"Bayes classifier flagged","ProcessingTimeMs":8.5}}]}}',
 0),
(82612, -1001322973935, date_trunc('day', NOW()) - INTERVAL '1 day' + INTERVAL '10 hours', 'System', 'StopWords', 3.75, 'Keyword pattern match',
 'spam-detection-v1', FALSE, 3.75,
 NULL,
 0),

-- Last week's spam (2 records) - use messages 82606, 82603
(82606, -1001322973935, date_trunc('day', NOW()) - INTERVAL '8 days' + INTERVAL '12 hours', 'System', 'StopWords', 4.0, 'Spam pattern detected',
 'spam-detection-v1', FALSE, 4.0,
 NULL,
 0),
(82603, -1001322973935, date_trunc('day', NOW()) - INTERVAL '9 days' + INTERVAL '12 hours', 'System', 'Bayes', 3.25, 'Spam probability exceeded threshold',
 'spam-detection-v1', FALSE, 3.25,
 NULL,
 0);


-- ============================================================
-- 2. MANUAL CORRECTIONS (for FP/FN accuracy tests)
-- These create correction records that override automated detections
-- ============================================================

-- False Positive correction: Message 82617 was flagged spam above, now corrected to ham
-- (detection_source='manual', net_score negative = ham)
INSERT INTO detection_results (message_id, chat_id, detected_at, detection_source, detection_method, score, reason, system_identifier, used_for_training, net_score, edit_version)
VALUES
(82617, -1001322973935, date_trunc('day', NOW()) + INTERVAL '4 seconds', 'manual', 'Manual Review', 5.0, 'Admin correction: not spam (false positive)', 'manual-review', FALSE, -5.0, 0);

-- First add a ham detection for message 82594 (required for FN test - needs original automated detection)
INSERT INTO detection_results (message_id, chat_id, detected_at, detection_source, detection_method, score, reason, system_identifier, used_for_training, net_score, edit_version)
VALUES
(82594, -1001322973935, date_trunc('day', NOW()) + INTERVAL '5 seconds', 'System', 'StopWords, Bayes', 0.0, 'No spam detected', 'spam-detection-v1', FALSE, 0.0, 0);

-- False Negative correction: Message 82594 was detected as ham above, now corrected to spam
-- (detection_source='manual', net_score positive = spam)
INSERT INTO detection_results (message_id, chat_id, detected_at, detection_source, detection_method, score, reason, system_identifier, used_for_training, net_score, edit_version)
VALUES
(82594, -1001322973935, date_trunc('day', NOW()) + INTERVAL '6 seconds', 'manual', 'Manual Review', 5.0, 'Admin correction: is spam (false negative)', 'manual-review', FALSE, 5.0, 0);


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
