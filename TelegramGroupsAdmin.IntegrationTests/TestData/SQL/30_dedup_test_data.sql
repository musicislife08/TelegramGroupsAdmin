-- Deduplication Test Data for SimHash Integration Tests
-- Contains intentional near-duplicates to test SimHash accuracy
-- Message IDs: 95001-95030 (to avoid conflicts with other test data)
-- Uses existing telegram_user_id and chat_id from GoldenDataset base data

-- Insert messages with near-duplicate spam patterns
INSERT INTO messages (message_id, user_id, chat_id, timestamp, message_text, content_check_skip_reason)
VALUES
-- Group 1: Crypto signal spam variants (should all be detected as near-duplicates)
-- Using longer text with minimal single-word changes for reliable SimHash similarity
(95001, 549755813888, -1002152207375, NOW() - INTERVAL '1 hour',
 'Join our exclusive telegram channel today for premium crypto trading signals and expert tips from professional traders', 0),
(95002, 549755813888, -1002152207375, NOW() - INTERVAL '2 hours',
 'Join our exclusive telegram channel today for premium crypto trading signals and expert tips from experienced traders', 0),
(95003, 549755813888, -1002152207375, NOW() - INTERVAL '3 hours',
 'Join our exclusive telegram channel today for premium crypto trading signals and expert advice from professional traders', 0),
(95004, 549755813888, -1002152207375, NOW() - INTERVAL '4 hours',
 'Join our exclusive telegram channel today for premium crypto trading signals and expert tips from verified traders', 0),

-- Group 2: Investment scam variants (single word changes only for reliable detection)
(95005, 549755813888, -1002152207375, NOW() - INTERVAL '5 hours',
 'Earn passive income with Bitcoin investment guaranteed returns daily', 0),
(95006, 549755813888, -1002152207375, NOW() - INTERVAL '6 hours',
 'Earn passive income with Ethereum investment guaranteed returns daily', 0),
(95007, 549755813888, -1002152207375, NOW() - INTERVAL '7 hours',
 'Earn passive income with Bitcoin investment guaranteed profits daily', 0),

-- Group 3: Giveaway scam variants (single word changes only)
(95008, 549755813888, -1002152207375, NOW() - INTERVAL '8 hours',
 'FREE Bitcoin giveaway click here to claim your prize now', 0),
(95009, 549755813888, -1002152207375, NOW() - INTERVAL '9 hours',
 'FREE Ethereum giveaway click here to claim your prize now', 0),
(95010, 549755813888, -1002152207375, NOW() - INTERVAL '10 hours',
 'FREE Bitcoin giveaway click here to claim your reward now', 0),

-- Group 4: Different spam topics (should NOT be near-duplicates of Groups 1-3)
(95011, 549755813888, -1002152207375, NOW() - INTERVAL '11 hours',
 'Hot singles in your area want to meet you click to chat now', 0),
(95012, 549755813888, -1002152207375, NOW() - INTERVAL '12 hours',
 'Pharmacy discounts available get prescription meds without doctor visit', 0),
(95013, 549755813888, -1002152207375, NOW() - INTERVAL '13 hours',
 'Work from home earn five thousand weekly no experience required', 0),

-- Group 5: Legitimate ham messages (should NOT match any spam)
(95014, 549755813888, -1002152207375, NOW() - INTERVAL '14 hours',
 'Has anyone tried the new restaurant downtown looking for recommendations', 0),
(95015, 549755813888, -1002152207375, NOW() - INTERVAL '15 hours',
 'Just finished deploying the new feature monitoring metrics closely now', 0),
(95016, 549755813888, -1002152207375, NOW() - INTERVAL '16 hours',
 'The weather has been really nice lately perfect for hiking activities', 0),

-- Group 6: Ham near-duplicates (legitimate variations)
(95017, 549755813888, -1002152207375, NOW() - INTERVAL '17 hours',
 'Working on a machine learning project using Python and TensorFlow', 0),
(95018, 549755813888, -1002152207375, NOW() - INTERVAL '18 hours',
 'Working on a machine learning project using Python and PyTorch', 0),
(95019, 549755813888, -1002152207375, NOW() - INTERVAL '19 hours',
 'Working on a deep learning project using Python and TensorFlow', 0),

-- Group 7: More spam variants for threshold testing (single word changes only)
-- Using longer text with minimal changes for reliable SimHash detection
(95020, 549755813888, -1002152207375, NOW() - INTERVAL '20 hours',
 'Make money fast today with our proven automated trading system absolutely no risk involved guaranteed profits', 0),
(95021, 549755813888, -1002152207375, NOW() - INTERVAL '21 hours',
 'Make money fast today with our proven automated trading system absolutely no risk involved guaranteed returns', 0),
(95022, 549755813888, -1002152207375, NOW() - INTERVAL '22 hours',
 'Make money fast today with our proven automated trading system absolutely no risk involved guaranteed income', 0);

-- Insert training labels for the test messages
-- Group 1-3 and 7 are spam (label=0), Group 4 is spam too
-- Groups 5-6 are ham (label=1)
INSERT INTO training_labels (message_id, label, labeled_by_user_id, labeled_at)
VALUES
-- Spam labels
(95001, 0, NULL, NOW() - INTERVAL '1 hour'),
(95002, 0, NULL, NOW() - INTERVAL '2 hours'),
(95003, 0, NULL, NOW() - INTERVAL '3 hours'),
(95004, 0, NULL, NOW() - INTERVAL '4 hours'),
(95005, 0, NULL, NOW() - INTERVAL '5 hours'),
(95006, 0, NULL, NOW() - INTERVAL '6 hours'),
(95007, 0, NULL, NOW() - INTERVAL '7 hours'),
(95008, 0, NULL, NOW() - INTERVAL '8 hours'),
(95009, 0, NULL, NOW() - INTERVAL '9 hours'),
(95010, 0, NULL, NOW() - INTERVAL '10 hours'),
(95011, 0, NULL, NOW() - INTERVAL '11 hours'),
(95012, 0, NULL, NOW() - INTERVAL '12 hours'),
(95013, 0, NULL, NOW() - INTERVAL '13 hours'),
(95020, 0, NULL, NOW() - INTERVAL '20 hours'),
(95021, 0, NULL, NOW() - INTERVAL '21 hours'),
(95022, 0, NULL, NOW() - INTERVAL '22 hours'),
-- Ham labels
(95014, 1, NULL, NOW() - INTERVAL '14 hours'),
(95015, 1, NULL, NOW() - INTERVAL '15 hours'),
(95016, 1, NULL, NOW() - INTERVAL '16 hours'),
(95017, 1, NULL, NOW() - INTERVAL '17 hours'),
(95018, 1, NULL, NOW() - INTERVAL '18 hours'),
(95019, 1, NULL, NOW() - INTERVAL '19 hours');
