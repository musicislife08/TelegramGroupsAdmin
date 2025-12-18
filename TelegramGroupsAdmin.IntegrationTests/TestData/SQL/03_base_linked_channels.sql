-- Base Linked Channels (channels linked to managed chats) for Integration Tests
-- Contains 2 channels for impersonation detection testing
-- FK: managed_chat_id references managed_chats.chat_id
-- MUST be loaded after 02_base_managed_chats.sql

INSERT INTO linked_channels (managed_chat_id, channel_id, channel_name, channel_icon_path, photo_hash, last_synced)
VALUES
-- Channel1: Main Test Channel linked to MainChat (has photo hash for pHash comparison)
(-1001322973935, -1001555777999, 'Main Test Channel', NULL, E'\\x123456789ABCDEF0', NOW() - INTERVAL '1 day'),
-- Channel2: Alpha Channel linked to Chat1 (has icon path but no photo hash - tests null handling)
(-1001766988150, -1001444666888, 'Alpha Channel', 'channels/alpha_icon.jpg', NULL, NOW() - INTERVAL '2 days');
