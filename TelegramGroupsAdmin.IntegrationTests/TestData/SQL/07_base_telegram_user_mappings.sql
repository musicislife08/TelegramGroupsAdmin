-- Links the test Owner web user to a test Telegram user for WebAdmin bypass path coverage.
-- Consumed by WelcomeFlowBypassIntegrationTests.
-- FK: user_id -> users.id, telegram_id -> telegram_users.telegram_user_id

INSERT INTO telegram_user_mappings (telegram_id, telegram_username, user_id, linked_at, is_active)
VALUES (100001, 'alice_user', 'b388ee38-0ed3-4c09-9def-5715f9f07f56', NOW(), TRUE)
ON CONFLICT DO NOTHING;
