-- Test data for TelegramGroupsAdmin message_history.db
-- Run this against /data/message_history.db to populate with fake messages

-- Helper: Calculate Unix timestamp for relative dates
-- Current time (adjust this to your testing time if needed)
-- SELECT strftime('%s', 'now') as current_timestamp;

-- Insert fake users and messages
-- Timestamps are calculated as: now - X hours

-- Messages from last 24 hours
INSERT INTO messages (message_id, user_id, user_name, chat_id, timestamp, expires_at, message_text, photo_file_id, photo_file_size, urls, edit_date, content_hash, chat_name, photo_local_path, photo_thumbnail_path)
VALUES
-- Recent normal messages
(1001, 12345, 'john_doe', -1001234567890, strftime('%s', 'now', '-1 hour'), strftime('%s', 'now', '+23 hours'), 'Hey everyone! Just wanted to share this cool article I found about homelab setups.', NULL, NULL, 'https://example.com/homelab-guide', NULL, NULL, 'Homelab Enthusiasts', NULL, NULL),

(1002, 67890, 'alice_tech', -1001234567890, strftime('%s', 'now', '-2 hours'), strftime('%s', 'now', '+22 hours'), 'Has anyone tried running TrueNAS Scale with Proxmox? Looking for advice.', NULL, NULL, NULL, NULL, NULL, 'Homelab Enthusiasts', NULL, NULL),

(1003, 11111, 'bob_admin', -1001234567890, strftime('%s', 'now', '-3 hours'), strftime('%s', 'now', '+21 hours'), 'Check out my new server rack! 🔥', NULL, NULL, NULL, NULL, NULL, 'Homelab Enthusiasts', NULL, NULL),

-- Message with edit
(1004, 12345, 'john_doe', -1001234567890, strftime('%s', 'now', '-4 hours'), strftime('%s', 'now', '+20 hours'), 'My NAS is running perfectly now after the firmware update.', NULL, NULL, NULL, strftime('%s', 'now', '-3 hours'), NULL, 'Homelab Enthusiasts', NULL, NULL),

-- Spam-like messages (crypto scam pattern)
(1005, 99999, 'crypto_winner', -1001234567890, strftime('%s', 'now', '-5 hours'), strftime('%s', 'now', '+19 hours'), '🚀 URGENT! I just received $5,000 from this trading platform! Click here now before the offer ends! 💰💰💰', NULL, NULL, 'https://scam-crypto-site.xyz/register?ref=12345', NULL, 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855', 'Homelab Enthusiasts', NULL, NULL),

(1006, 88888, 'investment_guru', -1001234567890, strftime('%s', 'now', '-6 hours'), strftime('%s', 'now', '+18 hours'), 'Deposited $500, withdrew $8,000 in just 3 days! 📈 Join my investment group! Limited slots!', NULL, NULL, 'https://fake-invest.com/join', NULL, 'a1b2c3d4e5f6789012345678901234567890abcdef1234567890abcdef123456', 'Homelab Enthusiasts', NULL, NULL),

-- Normal tech discussion
(1007, 67890, 'alice_tech', -1001234567890, strftime('%s', 'now', '-7 hours'), strftime('%s', 'now', '+17 hours'), 'Anyone know good resources for learning Docker Compose? Working on my media server stack.', NULL, NULL, NULL, NULL, NULL, 'Homelab Enthusiasts', NULL, NULL),

(1008, 22222, 'network_ninja', -1001234567890, strftime('%s', 'now', '-8 hours'), strftime('%s', 'now', '+16 hours'), 'Just set up Wireguard VPN for remote access. Game changer!', NULL, NULL, 'https://github.com/wireguard/wireguard', NULL, NULL, 'Homelab Enthusiasts', NULL, NULL),

-- Message with image (photo_file_id populated)
(1009, 11111, 'bob_admin', -1001234567890, strftime('%s', 'now', '-9 hours'), strftime('%s', 'now', '+15 hours'), 'My cable management before/after:', 'AgACAgIAAxkBAAIBY2ZxN3Q5MTIzNDU2Nzg5MGFiY2RlZgAC', 245678, NULL, NULL, NULL, 'Homelab Enthusiasts', NULL, NULL),

-- Phishing attempt
(1010, 77777, 'telegram_support', -1001234567890, strftime('%s', 'now', '-10 hours'), strftime('%s', 'now', '+14 hours'), '⚠️ SECURITY ALERT: Your account will be suspended! Verify now: https://telegam-verify.net/login', NULL, NULL, 'https://telegam-verify.net/login', NULL, 'phish123456789abcdef0123456789abcdef0123456789abcdef0123456789ab', 'Homelab Enthusiasts', NULL, NULL),

-- Normal messages
(1011, 33333, 'storage_sam', -1001234567890, strftime('%s', 'now', '-11 hours'), strftime('%s', 'now', '+13 hours'), 'Running out of disk space again. Time to buy more drives! 💾', NULL, NULL, NULL, NULL, NULL, 'Homelab Enthusiasts', NULL, NULL),

(1012, 12345, 'john_doe', -1001234567890, strftime('%s', 'now', '-12 hours'), strftime('%s', 'now', '+12 hours'), 'What backup strategy does everyone use? Currently using Restic to Backblaze B2.', NULL, NULL, 'https://restic.net/', NULL, NULL, 'Homelab Enthusiasts', NULL, NULL),

-- Another spam (forex scam)
(1013, 66666, 'forex_master', -1001234567890, strftime('%s', 'now', '-13 hours'), strftime('%s', 'now', '+11 hours'), 'Turn $100 into $10,000! My forex signals are 98% accurate! DM me for VIP access! 💸💸💸', NULL, NULL, 'https://forex-scam.biz/signals', NULL, 'forexscam123456789abcdef0123456789abcdef0123456789abcdef012345', 'Homelab Enthusiasts', NULL, NULL),

-- Tech help
(1014, 44444, 'help_seeker', -1001234567890, strftime('%s', 'now', '-14 hours'), strftime('%s', 'now', '+10 hours'), 'Help! My Plex server keeps buffering. Running on a Raspberry Pi 4. Any tips?', NULL, NULL, NULL, NULL, NULL, 'Homelab Enthusiasts', NULL, NULL),

(1015, 67890, 'alice_tech', -1001234567890, strftime('%s', 'now', '-15 hours'), strftime('%s', 'now', '+9 hours'), '@help_seeker Try enabling hardware transcoding. Pi 4 can struggle with software transcoding.', NULL, NULL, 'https://support.plex.tv/articles/115002178853-using-hardware-accelerated-streaming/', NULL, NULL, 'Homelab Enthusiasts', NULL, NULL),

-- Multiple URLs in one message
(1016, 22222, 'network_ninja', -1001234567890, strftime('%s', 'now', '-16 hours'), strftime('%s', 'now', '+8 hours'), 'Great resources for beginners:\n- https://www.reddit.com/r/homelab\n- https://github.com/awesome-selfhosted/awesome-selfhosted\n- https://perfectmediaserver.com/', NULL, NULL, 'https://www.reddit.com/r/homelab,https://github.com/awesome-selfhosted/awesome-selfhosted,https://perfectmediaserver.com/', NULL, NULL, 'Homelab Enthusiasts', NULL, NULL),

-- Airdrop scam (common crypto spam)
(1017, 55555, 'airdrop_alert', -1001234567890, strftime('%s', 'now', '-17 hours'), strftime('%s', 'now', '+7 hours'), '🎉 FREE AIRDROP! Claim 1000 USDT now! Only first 100 users! Connect wallet here 👇', NULL, NULL, 'https://fake-airdrop.scam/connect', NULL, 'airdropscam123456789abcdef0123456789abcdef0123456789abcdef01234', 'Homelab Enthusiasts', NULL, NULL),

-- Normal discussion continues
(1018, 33333, 'storage_sam', -1001234567890, strftime('%s', 'now', '-18 hours'), strftime('%s', 'now', '+6 hours'), 'Anyone using ZFS? Thinking about migrating from ext4.', NULL, NULL, NULL, NULL, NULL, 'Homelab Enthusiasts', NULL, NULL),

(1019, 11111, 'bob_admin', -1001234567890, strftime('%s', 'now', '-19 hours'), strftime('%s', 'now', '+5 hours'), '@storage_sam ZFS is amazing! Snapshots and data integrity checks are worth it. RAM hungry though.', NULL, NULL, NULL, NULL, NULL, 'Homelab Enthusiasts', NULL, NULL),

-- Message from different chat
(1020, 12345, 'john_doe', -1001111111111, strftime('%s', 'now', '-20 hours'), strftime('%s', 'now', '+4 hours'), 'Wrong group! Anyone here using Kubernetes at home?', NULL, NULL, NULL, NULL, NULL, 'Cloud Native Homelab', NULL, NULL);

-- Add some message edits for message 1004
INSERT INTO message_edits (message_id, edit_date, old_text, new_text, old_content_hash, new_content_hash)
VALUES
(1004, strftime('%s', 'now', '-3 hours'), 'My NAS is running perfectly now.', 'My NAS is running perfectly now after the firmware update.', 'hash_old_1004', 'hash_new_1004');

-- Add some spam check results (simulating what would come from spam detection)
INSERT INTO spam_checks (check_timestamp, user_id, content_hash, is_spam, confidence, reason, check_type, matched_message_id)
VALUES
-- Message 1005 detected as spam
(strftime('%s', 'now', '-5 hours'), 99999, 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855', 1, 95, 'Crypto scam pattern detected: Urgency + money claims + suspicious URL', 'text', 1005),

-- Message 1006 detected as spam
(strftime('%s', 'now', '-6 hours'), 88888, 'a1b2c3d4e5f6789012345678901234567890abcdef1234567890abcdef123456', 1, 92, 'Investment scam: Unrealistic profit claims', 'text', 1006),

-- Message 1010 detected as spam
(strftime('%s', 'now', '-10 hours'), 77777, 'phish123456789abcdef0123456789abcdef0123456789abcdef0123456789ab', 1, 98, 'Phishing: Fake security alert with suspicious domain', 'text', 1010),

-- Message 1013 detected as spam
(strftime('%s', 'now', '-13 hours'), 66666, 'forexscam123456789abcdef0123456789abcdef0123456789abcdef012345', 1, 90, 'Forex scam: Unrealistic accuracy claims', 'text', 1013),

-- Message 1017 detected as spam
(strftime('%s', 'now', '-17 hours'), 55555, 'airdropscam123456789abcdef0123456789abcdef0123456789abcdef01234', 1, 94, 'Crypto airdrop scam: Urgency + free money claims', 'text', 1017),

-- Some clean messages checked
(strftime('%s', 'now', '-2 hours'), 67890, NULL, 0, 15, 'Clean message', 'text', 1002),
(strftime('%s', 'now', '-7 hours'), 67890, NULL, 0, 12, 'Clean message', 'text', 1007);

-- Verify data
SELECT 'Total messages inserted:' as info, COUNT(*) as count FROM messages;
SELECT 'Total spam checks:' as info, COUNT(*) as count FROM spam_checks;
SELECT 'Total edits:' as info, COUNT(*) as count FROM message_edits;

-- Show sample data
SELECT '=== Sample Messages ===' as info;
SELECT message_id, user_name, substr(message_text, 1, 50) || '...' as preview, timestamp
FROM messages
ORDER BY timestamp DESC
LIMIT 5;

SELECT '=== Spam Messages ===' as info;
SELECT m.message_id, m.user_name, s.confidence, s.reason
FROM messages m
JOIN spam_checks s ON m.message_id = s.matched_message_id
WHERE s.is_spam = 1
ORDER BY s.confidence DESC;
