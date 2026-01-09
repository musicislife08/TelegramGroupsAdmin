-- Base Managed Chats (Telegram groups monitored by bot) for Integration Tests
-- Contains 4 chats including the main test group
-- NO FK dependencies
-- Chat IDs use Telegram supergroup format (negative, starts with -100)

INSERT INTO managed_chats (chat_id, chat_name, chat_type, bot_status, is_admin, added_at, is_active, last_seen_at)
VALUES
-- Chat1: Test Group Alpha
(-1001766988150, 'Test Group Alpha', 2, 1, TRUE, NOW() - INTERVAL '13 days', TRUE, NOW()),
-- Chat2: Bot Testing Beta
(-1003193605358, 'Bot Testing Beta', 2, 1, TRUE, NOW() - INTERVAL '12 days', TRUE, NOW()),
-- Chat3: Community Gamma
(-1001241664237, 'Community Gamma', 2, 1, TRUE, NOW() - INTERVAL '11 days', TRUE, NOW()),
-- MainChat: Main Test Group (used by most test messages)
(-1001322973935, 'Main Test Group', 2, 1, TRUE, NOW() - INTERVAL '10 days', TRUE, NOW()),
-- Additional chat referenced by training data scripts
(-1002152207375, 'Training Data Chat', 2, 1, TRUE, NOW() - INTERVAL '9 days', TRUE, NOW());
