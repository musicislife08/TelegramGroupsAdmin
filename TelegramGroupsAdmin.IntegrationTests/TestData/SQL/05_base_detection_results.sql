-- Base Detection Results for Integration Tests
-- Contains 2 spam detection results (both classified as ham)
-- FK: message_id references messages.message_id
-- MUST be loaded after 04_base_messages.sql

INSERT INTO detection_results (message_id, chat_id, detected_at, detection_source, detection_method, score, reason, system_identifier, used_for_training, net_score, edit_version)
VALUES
-- Result1: Msg1 (82619) "Fair enough" - no spam detected (score 0)
(82619, -1001322973935, NOW() - INTERVAL '1 hour', 'System', 'InvisibleChars, StopWords, CAS, Similarity, Bayes, Spacing', 0.0, 'No spam detected', 'spam-detection-v1', FALSE, 0.0, 0),
-- Result2: Msg11 (82581) "I hit 30 last summer" - low score spam probability (1.85)
(82581, -1001322973935, NOW() - INTERVAL '2 hours', 'System', 'InvisibleChars, StopWords, CAS, Similarity, Bayes, Spacing', 1.85, 'Spam probability: 0.752 (key words: hit) (certainty: 0.504)', 'spam-detection-v1', FALSE, 1.85, 0);
